namespace LogViewer.Core.Structured;

/// <summary>One span in a SerilogTracing trace, plus its children in parent/child order.</summary>
public sealed record TraceSpanNode(
    string SpanId,
    string? ParentSpanId,
    string Name,
    string? Level,
    string? SpanKind,
    DateTimeOffset? StartTimestamp,
    TimeSpan? Duration,
    long LineNumber,
    IReadOnlyList<TraceSpanNode> Children);

/// <summary>One distinct trace found across a document's buffered lines, for the trace picker.</summary>
public sealed record TraceSummary(string TraceId, int SpanCount, DateTimeOffset? FirstSeen, long FirstLineNumber);

/// <summary>Builds the per-trace span hierarchy a SerilogTracing-instrumented app produces, from the flat
/// stream of parsed log lines a document holds. Pure/stateless so it can be exercised directly against
/// fixture lines in Core tests, independent of any WPF view-model.</summary>
public static class SerilogTraceTreeBuilder
{
    /// <summary>Groups every span-completion event by <see cref="StructuredLogEvent.TraceId"/>, newest first.</summary>
    public static IReadOnlyList<TraceSummary> SummarizeTraces(IEnumerable<(long LineNumber, StructuredLogEvent Event)> lines)
    {
        var byTrace = new Dictionary<string, (int Count, DateTimeOffset? FirstSeen, long FirstLineNumber)>();

        foreach (var (lineNumber, evt) in lines)
        {
            if (!evt.IsSpan || evt.TraceId is not { } traceId)
            {
                continue;
            }

            if (byTrace.TryGetValue(traceId, out var existing))
            {
                var firstSeen = existing.FirstSeen is { } prev && (evt.SpanStartTimestamp is null || prev <= evt.SpanStartTimestamp)
                    ? existing.FirstSeen
                    : evt.SpanStartTimestamp;
                byTrace[traceId] = (existing.Count + 1, firstSeen, Math.Min(existing.FirstLineNumber, lineNumber));
            }
            else
            {
                byTrace[traceId] = (1, evt.SpanStartTimestamp, lineNumber);
            }
        }

        return byTrace
            .Select(kv => new TraceSummary(kv.Key, kv.Value.Count, kv.Value.FirstSeen, kv.Value.FirstLineNumber))
            .OrderByDescending(t => t.FirstSeen)
            .ThenByDescending(t => t.FirstLineNumber)
            .ToList();
    }

    /// <summary>Builds the parent/child span tree for one trace id. Spans whose <c>ParentSpanId</c> is missing,
    /// or doesn't match another span in the same trace (the parent fell outside the buffered window), become roots.</summary>
    public static IReadOnlyList<TraceSpanNode> BuildTree(IEnumerable<(long LineNumber, StructuredLogEvent Event)> lines, string traceId)
    {
        var spans = new Dictionary<string, (StructuredLogEvent Event, long LineNumber)>();
        foreach (var (lineNumber, evt) in lines)
        {
            if (evt.IsSpan && evt.TraceId == traceId && evt.SpanId is { } spanId && !spans.ContainsKey(spanId))
            {
                spans[spanId] = (evt, lineNumber);
            }
        }

        var childrenByParent = new Dictionary<string, List<string>>();
        var roots = new List<string>();
        foreach (var (spanId, (evt, _)) in spans)
        {
            var parentId = evt.ParentSpanId;
            if (parentId is not null && spans.ContainsKey(parentId))
            {
                if (!childrenByParent.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    childrenByParent[parentId] = siblings;
                }

                siblings.Add(spanId);
            }
            else
            {
                roots.Add(spanId);
            }
        }

        List<TraceSpanNode> BuildLevel(IEnumerable<string> spanIds) =>
            spanIds
                .Select(id =>
                {
                    var (evt, lineNumber) = spans[id];
                    var children = childrenByParent.TryGetValue(id, out var kids) ? BuildLevel(kids) : [];
                    return new TraceSpanNode(id, evt.ParentSpanId, evt.MessageTemplate ?? evt.RenderedMessage, evt.Level,
                        evt.SpanKind, evt.SpanStartTimestamp, evt.SpanDuration, lineNumber, children);
                })
                .OrderBy(n => n.StartTimestamp)
                .ToList();

        return BuildLevel(roots);
    }
}

/// <summary>Formats a span's duration for display — milliseconds for anything under a second, seconds beyond that.</summary>
public static class SpanDurationFormatter
{
    public static string Format(TimeSpan duration) =>
        duration.TotalMilliseconds < 1000
            ? $"{duration.TotalMilliseconds:F1} ms"
            : $"{duration.TotalSeconds:F2} s";
}
