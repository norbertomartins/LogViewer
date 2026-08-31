using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace LogViewer.Core.Tailing;

/// <summary>Configuration for <see cref="EtwTailSource"/>.</summary>
public sealed class EtwTailOptions
{
    /// <summary>Provider name (e.g. <c>Microsoft-Windows-DotNETRuntime</c>) or a GUID string.</summary>
    public required string Provider { get; init; }

    /// <summary>Minimum event level to capture (1 = Critical … 5 = Verbose).</summary>
    public int Level { get; init; } = 4; // Informational

    /// <summary>Keyword bitmask; <see cref="ulong.MaxValue"/> captures every keyword.</summary>
    public ulong MatchAnyKeywords { get; init; } = ulong.MaxValue;

    public TimeSpan FlushInterval { get; init; } = TimeSpan.FromMilliseconds(200);
}

/// <summary>
/// Tails a real-time Event Tracing for Windows (ETW) provider. Requires an elevated (Administrator)
/// process — a clear error is raised otherwise. Each ETW event becomes one log line:
/// <c>HH:mm:ss.ffffff [Level] Provider/EventName key=value …</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EtwTailSource : ITailSource
{
    private readonly EtwTailOptions _options;
    private readonly string _sessionName;
    private readonly object _sync = new();
    private readonly List<string> _pendingLines = [];

    private TraceEventSession? _session;
    private Thread? _processingThread;
    private System.Threading.Timer? _flushTimer;
    private long _lineNumber;
    private bool _started;

    public EtwTailSource(EtwTailOptions options, string? sessionName = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sessionName = sessionName ?? $"LogViewer-ETW-{Guid.NewGuid():N}";
        DisplayName = $"[ETW] {options.Provider}";
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;

    // ETW real-time sessions have no truncation/rotation concept — part of the ITailSource contract.
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

            if (!OperatingSystem.IsWindows())
            {
                Error?.Invoke(this, new TailSourceErrorEventArgs(new PlatformNotSupportedException("ETW is only available on Windows.")));
                return;
            }

            if (!TraceEventSession.IsElevated().GetValueOrDefault())
            {
                Error?.Invoke(this, new TailSourceErrorEventArgs(
                    new UnauthorizedAccessException("Real-time ETW tracing requires running LogViewer as Administrator.")));
                return;
            }

            try
            {
                _session = new TraceEventSession(_sessionName) { StopOnDispose = true };
                _session.Source.Dynamic.All += OnEvent;

                if (Guid.TryParse(_options.Provider, out var providerGuid))
                {
                    _session.EnableProvider(providerGuid, (TraceEventLevel)_options.Level, _options.MatchAnyKeywords);
                }
                else
                {
                    _session.EnableProvider(_options.Provider, (TraceEventLevel)_options.Level, _options.MatchAnyKeywords);
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
                _session?.Dispose();
                _session = null;
                return;
            }

            _started = true;
            _flushTimer = new System.Threading.Timer(_ => FlushPending(), null, _options.FlushInterval, _options.FlushInterval);
            _processingThread = new Thread(ProcessLoop) { IsBackground = true, Name = "ETW-" + _sessionName };
            _processingThread.Start();
        }
    }

    public void Stop()
    {
        TraceEventSession? session;
        Thread? thread;
        lock (_sync)
        {
            if (!_started)
            {
                _session?.Dispose();
                _session = null;
                return;
            }

            _started = false;
            session = _session;
            _session = null;
            thread = _processingThread;
            _processingThread = null;
            _flushTimer?.Dispose();
            _flushTimer = null;
        }

        session?.Dispose(); // unblocks Source.Process()
        thread?.Join(TimeSpan.FromSeconds(2));
        FlushPending();
    }

    public void Dispose() => Stop();

    private void ProcessLoop()
    {
        try
        {
            _session?.Source.Process();
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
        }
    }

    private void OnEvent(TraceEvent data)
    {
        var line = new StringBuilder();
        line.Append(data.TimeStamp.ToString("HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
        line.Append(" [").Append(data.Level).Append("] ");
        line.Append(data.ProviderName).Append('/').Append(data.EventName);

        var message = data.FormattedMessage;
        if (!string.IsNullOrEmpty(message))
        {
            line.Append(' ').Append(message.Replace("\r", string.Empty).Replace('\n', ' '));
        }
        else
        {
            for (var i = 0; i < data.PayloadNames.Length; i++)
            {
                line.Append(' ').Append(data.PayloadNames[i]).Append('=').Append(data.PayloadString(i));
            }
        }

        lock (_sync)
        {
            _pendingLines.Add(line.ToString());
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
