namespace LogViewer.Core.Tailing;

/// <summary>How an <see cref="HttpTailSource"/> consumes its endpoint.</summary>
public enum HttpTailMode
{
    /// <summary>Pick <see cref="Stream"/> when the first response is chunked / <c>text/event-stream</c>,
    /// otherwise <see cref="Poll"/>.</summary>
    Auto,

    /// <summary>Hold one long-lived response open and emit lines as they arrive (SSE or chunked text).</summary>
    Stream,

    /// <summary>Re-request the URL on an interval and emit whatever lines are new since last time.</summary>
    Poll,
}

/// <summary>Configuration for <see cref="HttpTailSource"/>.</summary>
public sealed class HttpTailOptions
{
    public HttpTailMode Mode { get; init; } = HttpTailMode.Auto;

    /// <summary>Extra request headers (e.g. <c>Authorization</c>). Values are sent verbatim.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Interval between requests in <see cref="HttpTailMode.Poll"/> mode.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Base delay before reconnecting after a dropped stream / failed poll; grows linearly up to
    /// <see cref="MaxReconnectDelay"/>.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Flush cadence for the line batch buffer in stream mode.</summary>
    public TimeSpan StreamFlushInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Tails a log endpoint over HTTP(S) — either by holding one streaming response open (chunked text or
/// <c>text/event-stream</c>) or by polling a URL and emitting the lines that are new since the last
/// request. Reconnects with a linear backoff after a drop; a poll response that is shorter than or no
/// longer a prefix of what was already seen raises <see cref="SourceReset"/> (rotation), mirroring file
/// tailing.
/// </summary>
public sealed class HttpTailSource : ITailSource
{
    private readonly Uri _url;
    private readonly HttpTailOptions _options;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly object _sync = new();
    private readonly List<string> _pendingLines = [];

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private System.Threading.Timer? _flushTimer;
    private long _lineNumber;
    private int _emittedLineCount;
    private bool _started;

    public HttpTailSource(Uri url, HttpTailOptions? options = null, HttpMessageHandler? handler = null)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _options = options ?? new HttpTailOptions();
        _ownsClient = true;
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _client.Timeout = Timeout.InfiniteTimeSpan; // streaming responses must not time out
        DisplayName = url.ToString();
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;
    public event EventHandler<TailSourceResetEventArgs>? SourceReset;
    public event EventHandler<TailSourceErrorEventArgs>? Error;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _cts = new CancellationTokenSource();
            _flushTimer = new System.Threading.Timer(_ => FlushPending(), null, _options.StreamFlushInterval, _options.StreamFlushInterval);
            _worker = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            cts = _cts;
            _cts = null;
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        cts?.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // worker cancellation
        }

        cts?.Dispose();
        FlushPending();
    }

    public void Dispose()
    {
        Stop();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                var mode = _options.Mode;
                if (mode == HttpTailMode.Auto)
                {
                    mode = await ProbeModeAsync(token).ConfigureAwait(false);
                }

                if (mode == HttpTailMode.Stream)
                {
                    await StreamAsync(token).ConfigureAwait(false);
                }
                else
                {
                    await PollAsync(token).ConfigureAwait(false);
                }

                attempt = 0;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
            }

            attempt++;
            var delay = TimeSpan.FromTicks(Math.Min(
                _options.MaxReconnectDelay.Ticks,
                _options.ReconnectDelay.Ticks * attempt));
            await SafeDelay(delay, token).ConfigureAwait(false);
        }
    }

    private async Task<HttpTailMode> ProbeModeAsync(CancellationToken token)
    {
        using var request = BuildRequest();
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isStream = string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            || response.Headers.TransferEncodingChunked == true
            || response.Content.Headers.ContentLength is null;

        if (isStream)
        {
            await ConsumeStreamAsync(response, contentType, token).ConfigureAwait(false);
            return HttpTailMode.Stream;
        }

        // Not a stream — treat this first body as the initial poll snapshot, then keep polling.
        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        ApplyPollBody(body);
        return HttpTailMode.Poll;
    }

    private async Task StreamAsync(CancellationToken token)
    {
        using var request = BuildRequest();
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await ConsumeStreamAsync(response, response.Content.Headers.ContentType?.MediaType, token).ConfigureAwait(false);
    }

    private async Task ConsumeStreamAsync(HttpResponseMessage response, string? contentType, CancellationToken token)
    {
        var isSse = string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                break; // server closed the stream — RunAsync will reconnect
            }

            if (isSse)
            {
                if (line.Length == 0 || line.StartsWith(':') || line.StartsWith("event:") || line.StartsWith("id:") || line.StartsWith("retry:"))
                {
                    continue;
                }

                if (line.StartsWith("data:"))
                {
                    line = line[5..].TrimStart();
                }
            }

            QueuePending(line);
        }
    }

    private async Task PollAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await SafeDelay(_options.PollInterval, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            using var request = BuildRequest();
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            ApplyPollBody(body);
        }
    }

    private void ApplyPollBody(string body)
    {
        var lines = SplitLines(body);

        int newFrom;
        lock (_sync)
        {
            if (lines.Count < _emittedLineCount)
            {
                // The endpoint's content shrank — treat as a rotation.
                _emittedLineCount = 0;
                SourceReset?.Invoke(this, new TailSourceResetEventArgs(TailResetReason.Truncated));
            }

            newFrom = _emittedLineCount;
            _emittedLineCount = lines.Count;
        }

        for (var i = newFrom; i < lines.Count; i++)
        {
            QueuePending(lines[i]);
        }

        FlushPending();
    }

    private static List<string> SplitLines(string body)
    {
        var normalized = body.Replace("\r\n", "\n");
        var parts = normalized.Split('\n');
        // A trailing newline yields a final empty element that isn't a real line.
        var list = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i == parts.Length - 1 && parts[i].Length == 0)
            {
                break;
            }

            list.Add(parts[i]);
        }

        return list;
    }

    private void QueuePending(string line)
    {
        lock (_sync)
        {
            _pendingLines.Add(line);
        }
    }

    private void FlushPending()
    {
        List<TailLine> batch;
        lock (_sync)
        {
            if (_pendingLines.Count == 0)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            batch = new List<TailLine>(_pendingLines.Count);
            foreach (var text in _pendingLines)
            {
                batch.Add(new TailLine(++_lineNumber, 0, text, now));
            }

            _pendingLines.Clear();
        }

        LinesRead?.Invoke(this, new TailLinesReadEventArgs(batch));
    }

    private HttpRequestMessage BuildRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _url);
        foreach (var (name, value) in _options.Headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return request;
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
