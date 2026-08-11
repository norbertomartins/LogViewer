using LogViewer.Core.Structured;

namespace LogViewer.Core.BlockDiff;

/// <summary>Suggests which property on a <see cref="StructuredLogEvent"/> is likely a correlation id
/// (TraceId/CorrelationId/etc.) that groups a whole operation's log lines together. Feeds the
/// auto-suggested (but user-overridable) correlation field picker in the UI.</summary>
public static class CorrelationKeySelector
{
    /// <summary>Known correlation-style property names, in suggestion priority order.</summary>
    public static readonly IReadOnlyList<string> KnownNames =
    [
        "TraceId", "CorrelationId", "RequestId", "OperationId", "ActivityId",
        "SpanId", "TransactionId", "JobId", "SessionId",
    ];

    /// <summary>Returns the event's property keys that look like a correlation id: exact (case-insensitive)
    /// matches against <see cref="KnownNames"/> first (in that priority order), then any other key ending in
    /// "Id"/"Guid", alphabetically.</summary>
    public static IReadOnlyList<string> SuggestFields(StructuredLogEvent evt)
    {
        var keys = evt.Properties.Keys;

        var known = new List<string>();
        foreach (var name in KnownNames)
        {
            var match = keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                known.Add(match);
            }
        }

        var others = keys
            .Where(k => !known.Contains(k, StringComparer.OrdinalIgnoreCase)
                        && (k.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || k.EndsWith("Guid", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

        return [.. known, .. others];
    }
}
