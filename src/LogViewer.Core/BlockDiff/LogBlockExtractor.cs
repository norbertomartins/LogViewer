using LogViewer.Core.Structured;

namespace LogViewer.Core.BlockDiff;

/// <summary>Builds the "anchor" <see cref="LogBlock"/> from a document's already-parsed structured lines
/// (in-memory, not a file scan) — the block the user right-clicked into, that <see cref="ISimilarBlockFinder"/>
/// then looks for a match of in another file.</summary>
public static class LogBlockExtractor
{
    /// <summary>Every event whose <paramref name="correlationField"/> resolves to <paramref name="correlationValue"/>,
    /// in original order.</summary>
    public static LogBlock ExtractByCorrelation(
        IReadOnlyList<(long LineNumber, StructuredLogEvent Event)> events,
        string correlationField,
        string correlationValue,
        string sourceDescription)
    {
        var lines = events
            .Where(e => string.Equals(StructuredFieldResolver.Resolve(e.Event, correlationField), correlationValue, StringComparison.Ordinal))
            .Select(e => new LogBlockLine(e.LineNumber, MessageSignature.Compute(e.Event), e.Event))
            .ToList();

        return new LogBlock(lines, correlationField, correlationValue, sourceDescription);
    }

    /// <summary>Fallback when no correlation field is available: walks outward from <paramref name="anchorIndex"/>
    /// while consecutive events stay within <paramref name="maxGap"/> of each other's timestamp and (if the anchor
    /// has one) share its ThreadId; falls back to a pure line-count window when timestamps are absent. Bounded to
    /// <paramref name="maxLinesEachDirection"/> lines in either direction.</summary>
    public static LogBlock ExtractByProximity(
        IReadOnlyList<(long LineNumber, StructuredLogEvent Event)> events,
        int anchorIndex,
        string sourceDescription,
        TimeSpan maxGap,
        int maxLinesEachDirection = 200)
    {
        if (anchorIndex < 0 || anchorIndex >= events.Count)
        {
            return new LogBlock([], null, null, sourceDescription);
        }

        var anchorThreadId = StructuredFieldResolver.Resolve(events[anchorIndex].Event, "ThreadId");

        var startIndex = anchorIndex;
        for (var i = anchorIndex - 1; i >= 0 && anchorIndex - i <= maxLinesEachDirection; i--)
        {
            if (!CanExtendTo(events[i], events[i + 1], anchorThreadId, maxGap))
            {
                break;
            }

            startIndex = i;
        }

        var endIndex = anchorIndex;
        for (var i = anchorIndex + 1; i < events.Count && i - anchorIndex <= maxLinesEachDirection; i++)
        {
            if (!CanExtendTo(events[i], events[i - 1], anchorThreadId, maxGap))
            {
                break;
            }

            endIndex = i;
        }

        var lines = events
            .Skip(startIndex)
            .Take(endIndex - startIndex + 1)
            .Select(e => new LogBlockLine(e.LineNumber, MessageSignature.Compute(e.Event), e.Event))
            .ToList();

        return new LogBlock(lines, null, null, sourceDescription);
    }

    /// <summary>Whether <paramref name="candidate"/> can be pulled into the block being grown outward from
    /// <paramref name="neighbor"/> (the event already accepted, closer to the anchor).</summary>
    private static bool CanExtendTo(
        (long LineNumber, StructuredLogEvent Event) candidate,
        (long LineNumber, StructuredLogEvent Event) neighbor,
        string? anchorThreadId,
        TimeSpan maxGap)
    {
        if (anchorThreadId is not null)
        {
            var candidateThreadId = StructuredFieldResolver.Resolve(candidate.Event, "ThreadId");
            if (!string.Equals(candidateThreadId, anchorThreadId, StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (candidate.Event.Timestamp is { } candidateTs && neighbor.Event.Timestamp is { } neighborTs)
        {
            return (candidateTs - neighborTs).Duration() <= maxGap;
        }

        // No timestamps to compare on one/both sides — already bounded by the caller's line-count
        // guard, so accept purely on proximity-by-line-count.
        return true;
    }
}
