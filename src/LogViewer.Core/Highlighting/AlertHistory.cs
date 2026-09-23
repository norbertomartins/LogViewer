namespace LogViewer.Core.Highlighting;

public enum AlertKind
{
    /// <summary>A highlight rule matched <c>AlertThresholdCount</c> times within its window.</summary>
    Threshold,

    /// <summary>A Warning/Error message shape was seen for the first time after the document's initial load.</summary>
    NewPattern,
}

/// <summary>One alert a document raised: when, why, and the line that triggered it.</summary>
public sealed record AlertRecord(DateTimeOffset RaisedAt, AlertKind Kind, string? RuleName, long LineNumber, string LineText);

/// <summary>
/// The most recent alerts one document raised (oldest dropped past <see cref="Capacity"/>), kept whether or not
/// desktop notifications are enabled so the MCP <c>logs_get_alerts</c> tool can report what fired. Thread-safe:
/// recorded on the UI thread, read from MCP request threads.
/// </summary>
public sealed class AlertHistory(int capacity = AlertHistory.DefaultCapacity)
{
    public const int DefaultCapacity = 200;

    private readonly object _sync = new();
    private readonly Queue<AlertRecord> _records = new();

    public int Capacity { get; } = capacity;

    public void Record(AlertRecord record)
    {
        lock (_sync)
        {
            _records.Enqueue(record);
            while (_records.Count > Capacity)
            {
                _records.Dequeue();
            }
        }
    }

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<AlertRecord> Snapshot()
    {
        lock (_sync)
        {
            return [.. _records];
        }
    }
}
