using System.Diagnostics;

namespace LogViewer.Core.Tailing;

/// <summary>Configuration for <see cref="ProcessTailSource"/>.</summary>
public sealed class ProcessTailOptions
{
    public required string FileName { get; init; }

    public string Arguments { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Fold the process's stderr into the line stream (many log tools — <c>journalctl</c>,
    /// <c>kubectl logs</c> — write diagnostics there).</summary>
    public bool IncludeStandardError { get; init; } = true;

    /// <summary>Relaunch the process when it exits (a <c>-f</c>/follow command that drops its connection).</summary>
    public bool RestartOnExit { get; init; } = true;

    /// <summary>Base delay before relaunching; grows linearly up to <see cref="MaxRestartDelay"/>.</summary>
    public TimeSpan RestartDelay { get; init; } = TimeSpan.FromSeconds(3);

    public TimeSpan MaxRestartDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Human label for <see cref="ITailSource.DisplayName"/>; defaults to <c>FileName Arguments</c>.</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// Tails the stdout (and optionally stderr) of a spawned process — <c>journalctl -f</c>,
/// <c>docker logs -f</c>, <c>kubectl logs -f</c>, <c>adb logcat</c>, etc. Each line the process writes
/// is emitted as a log line; when a follow-style command exits it is relaunched after a linear backoff.
/// </summary>
public sealed class ProcessTailSource : ITailSource
{
    private readonly ProcessTailOptions _options;
    private readonly object _sync = new();
    private readonly List<string> _pendingLines = [];

    private Process? _process;
    private System.Threading.Timer? _flushTimer;
    private CancellationTokenSource? _cts;
    private Task? _supervisor;
    private long _lineNumber;
    private bool _started;
    private int _consecutiveFailures;

    public ProcessTailSource(ProcessTailOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        DisplayName = options.DisplayName ?? $"{options.FileName} {options.Arguments}".Trim();
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;

    // A process stream has no truncation/rotation concept — part of the ITailSource contract, not dead code.
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
            _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));
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
        KillCurrentProcess();

        try
        {
            _supervisor?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // supervisor cancellation
        }

        cts?.Dispose();
        FlushPending();
    }

    public void Dispose() => Stop();

    private async Task SuperviseAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var exitCode = await RunOnceAsync(token).ConfigureAwait(false);
            if (token.IsCancellationRequested || !_options.RestartOnExit)
            {
                if (!token.IsCancellationRequested && exitCode is { } code and not 0)
                {
                    Error?.Invoke(this, new TailSourceErrorEventArgs(
                        new InvalidOperationException($"{_options.FileName} exited with code {code}.")));
                }

                return;
            }

            _consecutiveFailures = exitCode is 0 or null ? 0 : _consecutiveFailures + 1;
            var multiplier = Math.Max(1, _consecutiveFailures);
            var delay = TimeSpan.FromTicks(Math.Min(_options.MaxRestartDelay.Ticks, _options.RestartDelay.Ticks * multiplier));

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

    private async Task<int?> RunOnceAsync(CancellationToken token)
    {
        Process process;
        try
        {
            process = StartProcess();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
            _consecutiveFailures++;
            return null;
        }

        lock (_sync)
        {
            _process = process;
        }

        process.OutputDataReceived += OnLineReceived;
        if (_options.IncludeStandardError)
        {
            process.ErrorDataReceived += OnLineReceived;
        }

        process.BeginOutputReadLine();
        if (_options.IncludeStandardError)
        {
            process.BeginErrorReadLine();
        }

        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            process.OutputDataReceived -= OnLineReceived;
            process.ErrorDataReceived -= OnLineReceived;
            lock (_sync)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }
            }

            process.Dispose();
        }
    }

    private Process StartProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.FileName,
            Arguments = _options.Arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _options.WorkingDirectory ?? string.Empty,
        };

        foreach (var (name, value) in _options.Environment)
        {
            startInfo.Environment[name] = value;
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {_options.FileName}.");
    }

    private void OnLineReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return; // stream closed
        }

        lock (_sync)
        {
            _pendingLines.Add(e.Data);
        }
    }

    private void KillCurrentProcess()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
        }

        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        catch (NotSupportedException)
        {
            // platform without process-tree kill
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
