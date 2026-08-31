using System.Net.WebSockets;
using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>Configuration for <see cref="WebSocketTailSource"/>.</summary>
public sealed class WebSocketTailOptions
{
    /// <summary>Extra handshake request headers (e.g. <c>Authorization</c>). Sent verbatim.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Base delay before reconnecting after a drop; grows linearly up to <see cref="MaxReconnectDelay"/>.</summary>
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Flush cadence for the line batch buffer.</summary>
    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Receive buffer size, in bytes.</summary>
    public int ReceiveBufferSize { get; init; } = 16 * 1024;
}

/// <summary>
/// Tails a log stream delivered over a WebSocket (<c>ws://</c> / <c>wss://</c>). Each complete text
/// message is treated as one or more whole lines (split on <c>\n</c>, message end is an implicit line
/// break), so servers that send one line per frame and servers that batch lines both work. Reconnects
/// with a linear backoff after the socket closes or faults.
/// </summary>
public sealed class WebSocketTailSource : ITailSource
{
    private readonly Uri _url;
    private readonly WebSocketTailOptions _options;
    private readonly Func<WebSocket> _socketFactory;
    private readonly bool _factoryOwnsSocket;
    private readonly object _sync = new();
    private readonly List<string> _pendingLines = [];

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private System.Threading.Timer? _flushTimer;
    private long _lineNumber;
    private bool _started;

    public WebSocketTailSource(Uri url, WebSocketTailOptions? options = null, Func<WebSocket>? socketFactory = null)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _options = options ?? new WebSocketTailOptions();
        _factoryOwnsSocket = socketFactory is null;
        _socketFactory = socketFactory ?? (() =>
        {
            var socket = new ClientWebSocket();
            foreach (var (name, value) in _options.Headers)
            {
                socket.Options.SetRequestHeader(name, value);
            }

            return socket;
        });
        DisplayName = url.ToString();
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;

    // A WebSocket stream has no truncation/rotation concept — part of the ITailSource contract, not dead code.
#pragma warning disable CS0067
    public event EventHandler<TailSourceResetEventArgs>? SourceReset;
#pragma warning restore CS0067

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
            _flushTimer = new System.Threading.Timer(_ => FlushPending(), null, _options.FlushInterval, _options.FlushInterval);
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

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationToken token)
    {
        var attempt = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(token).ConfigureAwait(false);
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
            var delay = TimeSpan.FromTicks(Math.Min(_options.MaxReconnectDelay.Ticks, _options.ReconnectDelay.Ticks * attempt));
            try
            {
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectAndReceiveAsync(CancellationToken token)
    {
        var socket = _socketFactory();
        try
        {
            if (socket is ClientWebSocket client && client.State == WebSocketState.None)
            {
                await client.ConnectAsync(_url, token).ConfigureAwait(false);
            }

            var buffer = new byte[_options.ReceiveBufferSize];
            var message = new List<byte>();

            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), token).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return; // RunAsync reconnects
                }

                message.AddRange(buffer.AsSpan(0, result.Count).ToArray());

                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    foreach (var line in WebSocketFrames.SplitMessage(Encoding.UTF8.GetString(message.ToArray())))
                    {
                        QueuePending(line);
                    }
                }

                message.Clear();
            }
        }
        finally
        {
            if (_factoryOwnsSocket)
            {
                socket.Dispose();
            }
        }
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
}

/// <summary>Splits a WebSocket text message into log lines. Message end is treated as an implicit line
/// break, so a frame carrying a single un-terminated line still yields that line.</summary>
public static class WebSocketFrames
{
    public static IReadOnlyList<string> SplitMessage(string message)
    {
        if (message.Length == 0)
        {
            return [];
        }

        var parts = message.Replace("\r\n", "\n").Split('\n');
        var lines = new List<string>(parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            // A trailing newline produces a final empty element that isn't a real line.
            if (i == parts.Length - 1 && parts[i].Length == 0)
            {
                break;
            }

            lines.Add(parts[i].TrimEnd('\r'));
        }

        return lines;
    }
}
