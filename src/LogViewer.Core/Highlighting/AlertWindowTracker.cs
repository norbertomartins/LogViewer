namespace LogViewer.Core.Highlighting;

/// <summary>
/// Tracks, per highlight rule, how many times it has matched within a trailing time window, so a
/// caller can raise a threshold alert (e.g. a desktop notification) once a rule matches
/// <c>thresholdCount</c> times within <c>window</c> — a burst of matching lines fires the alert exactly
/// once, not once per line, since the queue is cleared the moment the threshold is reached.
/// </summary>
public sealed class AlertWindowTracker
{
    private readonly Dictionary<Guid, Queue<DateTime>> _hits = [];

    /// <summary>Records one match at <paramref name="timestamp"/> for <paramref name="ruleId"/>, drops
    /// hits older than <paramref name="window"/>, and returns true — clearing the tracked hits — the
    /// moment the remaining count reaches <paramref name="thresholdCount"/>.</summary>
    public bool RecordHit(Guid ruleId, DateTime timestamp, int thresholdCount, TimeSpan window)
    {
        if (!_hits.TryGetValue(ruleId, out var queue))
        {
            queue = new Queue<DateTime>();
            _hits[ruleId] = queue;
        }

        queue.Enqueue(timestamp);
        while (queue.Count > 0 && timestamp - queue.Peek() > window)
        {
            queue.Dequeue();
        }

        if (queue.Count < thresholdCount)
        {
            return false;
        }

        queue.Clear();
        return true;
    }
}
