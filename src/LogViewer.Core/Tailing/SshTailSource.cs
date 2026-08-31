using Renci.SshNet;

namespace LogViewer.Core.Tailing;

/// <summary>Configuration for <see cref="SshTailSource"/>. Secrets (<see cref="Password"/>,
/// <see cref="PrivateKeyPassphrase"/>) are held only for the life of the source and are never persisted
/// by the app — the settings layer stores host/port/user/key-path/command/fingerprint only.</summary>
public sealed class SshTailOptions
{
    public required string Host { get; init; }

    public int Port { get; init; } = 22;

    public required string Username { get; init; }

    public string? Password { get; init; }

    public string? PrivateKeyPath { get; init; }

    public string? PrivateKeyPassphrase { get; init; }

    /// <summary>The remote command to run, e.g. <c>tail -n 200 -F /var/log/syslog</c> or
    /// <c>journalctl -f -o cat</c>.</summary>
    public required string Command { get; init; }

    /// <summary>Expected SHA-256 host-key fingerprint (base64, as OpenSSH prints it). When null and
    /// <see cref="AcceptAnyHostKey"/> is false, the connection is refused with guidance.</summary>
    public string? ExpectedHostKeyFingerprintSha256 { get; init; }

    /// <summary>Trust any host key (skips the fingerprint check). Convenient but vulnerable to MITM.</summary>
    public bool AcceptAnyHostKey { get; init; }

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(150);
}

/// <summary>
/// Tails the output of a command run over SSH — <c>tail -F</c> a remote file, <c>journalctl -f</c> a
/// remote journal, etc. Streams the command's stdout (and stderr) line by line and reconnects with a
/// linear backoff if the SSH connection drops.
/// </summary>
public sealed class SshTailSource : ITailSource
{
    private readonly SshTailOptions _options;
    private readonly object _sync = new();
    private readonly List<string> _pendingLines = [];

    private CancellationTokenSource? _cts;
    private Task? _worker;
    private System.Threading.Timer? _flushTimer;
    private long _lineNumber;
    private bool _started;

    public SshTailSource(SshTailOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        DisplayName = $"{options.Username}@{options.Host}: {options.Command}";
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;

    // A remote command stream has no truncation/rotation concept — part of the ITailSource contract.
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
            _worker?.Wait(TimeSpan.FromSeconds(3));
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
                await RunSessionAsync(token).ConfigureAwait(false);
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

    private async Task RunSessionAsync(CancellationToken token)
    {
        using var client = new SshClient(BuildConnectionInfo());
        var hostKeyRejected = (string?)null;
        client.HostKeyReceived += (_, e) =>
        {
            if (_options.AcceptAnyHostKey)
            {
                e.CanTrust = true;
                return;
            }

            if (string.IsNullOrEmpty(_options.ExpectedHostKeyFingerprintSha256))
            {
                e.CanTrust = false;
                hostKeyRejected = $"Host key not verified. Set the expected SHA-256 fingerprint (server offered "
                    + $"'{e.FingerPrintSHA256}') or enable 'accept any host key'.";
                return;
            }

            e.CanTrust = string.Equals(e.FingerPrintSHA256, _options.ExpectedHostKeyFingerprintSha256, StringComparison.Ordinal);
            if (!e.CanTrust)
            {
                hostKeyRejected = $"Host key mismatch. Expected '{_options.ExpectedHostKeyFingerprintSha256}', "
                    + $"server offered '{e.FingerPrintSHA256}'.";
            }
        };

        await client.ConnectAsync(token).ConfigureAwait(false);
        if (hostKeyRejected is not null)
        {
            throw new InvalidOperationException(hostKeyRejected);
        }

        using var command = client.CreateCommand(_options.Command);
        var async = command.BeginExecute();

        using var stdout = new StreamReader(command.OutputStream);
        using var stderr = new StreamReader(command.ExtendedOutputStream);

        var pump = Task.WhenAll(PumpAsync(stdout, token), PumpAsync(stderr, token));

        while (!async.IsCompleted && !token.IsCancellationRequested)
        {
            await Task.Delay(100, token).ConfigureAwait(false);
        }

        if (token.IsCancellationRequested)
        {
            command.CancelAsync();
        }

        await pump.ConfigureAwait(false);
        command.EndExecute(async);
    }

    private async Task PumpAsync(StreamReader reader, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            lock (_sync)
            {
                _pendingLines.Add(line);
            }
        }
    }

    private ConnectionInfo BuildConnectionInfo()
    {
        var methods = new List<AuthenticationMethod>();

        if (!string.IsNullOrEmpty(_options.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(_options.PrivateKeyPassphrase)
                ? new PrivateKeyFile(_options.PrivateKeyPath)
                : new PrivateKeyFile(_options.PrivateKeyPath, _options.PrivateKeyPassphrase);
            methods.Add(new PrivateKeyAuthenticationMethod(_options.Username, keyFile));
        }

        if (!string.IsNullOrEmpty(_options.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(_options.Username, _options.Password));
        }

        if (methods.Count == 0)
        {
            throw new InvalidOperationException("SSH tail needs a password or a private key.");
        }

        return new ConnectionInfo(_options.Host, _options.Port, _options.Username, [.. methods]);
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
