using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.EventLogging;

/// <summary>
/// Tails a Windows Event Log channel (e.g. "Application", "System") live via
/// <see cref="EventLogWatcher"/>. Standard channels grant read to <c>BUILTIN\Users</c> by default,
/// so this works without administrator rights; the Security channel and some custom app channels
/// have restrictive ACLs and surface a clear "requires elevated permissions" error instead of
/// failing silently.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogSource : IEventLogSource
{
    private readonly string _channelName;
    private readonly TailSourceOptions _options;
    private readonly object _sync = new();

    private List<EventLogFilterRule> _filters;
    private EventLogWatcher? _watcher;
    private long _lineNumber;
    private bool _started;

    public WindowsEventLogSource(string channelName, IEnumerable<EventLogFilterRule>? filters = null, TailSourceOptions? options = null)
    {
        _channelName = channelName;
        _filters = filters?.ToList() ?? [];
        _options = options ?? new TailSourceOptions();
        DisplayName = channelName;
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;

    // EventLog channels have no truncation/rotation concept, so this never fires — it's part of the
    // ITailSource contract, not dead functionality.
#pragma warning disable CS0067
    public event EventHandler<TailSourceResetEventArgs>? SourceReset;
#pragma warning restore CS0067

    public event EventHandler<TailSourceErrorEventArgs>? Error;

    /// <summary>Replaces the active filter set. Takes effect for events observed from now on.</summary>
    public void SetFilters(IEnumerable<EventLogFilterRule> filters)
    {
        lock (_sync)
        {
            _filters = filters.ToList();
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;

            try
            {
                ReadInitialEntries();

                var watcher = new EventLogWatcher(new EventLogQuery(_channelName, PathType.LogName));
                watcher.EventRecordWritten += OnEventRecordWritten;
                watcher.Enabled = true;
                _watcher = watcher;
            }
            catch (EventLogNotFoundException ex)
            {
                _started = false;
                Error?.Invoke(this, new TailSourceErrorEventArgs(
                    new InvalidOperationException($"Event log channel '{_channelName}' was not found.", ex)));
            }
            catch (UnauthorizedAccessException ex)
            {
                _started = false;
                Error?.Invoke(this, new TailSourceErrorEventArgs(
                    new UnauthorizedAccessException($"Access to event log channel '{_channelName}' was denied — this channel may require elevated permissions.", ex)));
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            if (_watcher is not null)
            {
                _watcher.EventRecordWritten -= OnEventRecordWritten;
                _watcher.Enabled = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    public void Dispose() => Stop();

    private void ReadInitialEntries()
    {
        using var reader = new EventLogReader(new EventLogQuery(_channelName, PathType.LogName) { ReverseDirection = true });

        var wanted = Math.Max(_options.InitialTailLineCount, 0);
        var recent = new List<(DateTimeOffset Timestamp, string Text)>();

        for (var i = 0; i < wanted; i++)
        {
            using var record = reader.ReadEvent();
            if (record is null)
            {
                break;
            }

            var formatted = FormatRecord(record);
            if (formatted is not null && PassesFilters(record, formatted))
            {
                var timestamp = record.TimeCreated.HasValue ? new DateTimeOffset(record.TimeCreated.Value) : DateTimeOffset.UtcNow;
                recent.Add((timestamp, formatted));
            }
        }

        recent.Reverse();

        var lines = new List<TailLine>(recent.Count);
        foreach (var (timestamp, text) in recent)
        {
            _lineNumber++;
            lines.Add(new TailLine(_lineNumber, 0, text, timestamp));
        }

        if (lines.Count > 0)
        {
            LinesRead?.Invoke(this, new TailLinesReadEventArgs(lines));
        }
    }

    private void OnEventRecordWritten(object? sender, EventRecordWrittenEventArgs e)
    {
        if (e.EventException is not null)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(e.EventException));
            return;
        }

        var record = e.EventRecord;
        if (record is null)
        {
            return;
        }

        using (record)
        {
            var formatted = FormatRecord(record);
            if (formatted is null || !PassesFilters(record, formatted))
            {
                return;
            }

            _lineNumber++;
            var timestamp = record.TimeCreated.HasValue ? new DateTimeOffset(record.TimeCreated.Value) : DateTimeOffset.UtcNow;
            var line = new TailLine(_lineNumber, 0, formatted, timestamp);
            LinesRead?.Invoke(this, new TailLinesReadEventArgs([line]));
        }
    }

    private static string? FormatRecord(EventRecord record) => EventRecordFormatter.Format(record);

    /// <summary>No enabled filter means everything passes; otherwise an event must match at least one enabled filter.</summary>
    private bool PassesFilters(EventRecord record, string formattedMessage)
    {
        List<EventLogFilterRule> filters;
        lock (_sync)
        {
            filters = _filters;
        }

        return EventLogFilterEvaluator.PassesFilters(record, formattedMessage, filters);
    }
}
