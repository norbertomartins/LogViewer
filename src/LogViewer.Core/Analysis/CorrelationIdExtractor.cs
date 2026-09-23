using System.Text.RegularExpressions;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Analysis;

/// <summary>A correlation identifier found on a log line — <see cref="Name"/> is the key it appeared under
/// (e.g. <c>TraceId</c>, <c>request_id</c>), or <c>GUID</c> for a bare GUID in free text.</summary>
public sealed record CorrelationId(string Name, string Value);

/// <summary>
/// Pulls correlation identifiers (trace/span/request/correlation/operation/session ids …) out of a log line,
/// structured or not, so the user can filter every line that shares one with a single click. Structured
/// events contribute their native <see cref="StructuredLogEvent.TraceId"/>/<see cref="StructuredLogEvent.SpanId"/>
/// and any property whose name ends in a known id suffix; free text contributes <c>key=value</c> /
/// <c>"key": "value"</c> pairs with such a key, W3C <c>traceparent</c> headers, and bare GUIDs.
/// </summary>
public static partial class CorrelationIdExtractor
{
    public const int MaxIds = 10;

    /// <summary>Normalized (lower-case, alphanumerics only) key suffixes treated as correlation ids.</summary>
    private static readonly string[] KnownKeySuffixes =
    [
        "traceid", "spanid", "parentid", "correlationid", "requestid", "reqid", "operationid", "activityid",
        "transactionid", "txid", "txnid", "sessionid", "conversationid", "messageid", "jobid", "runid", "traceparent",
    ];

    [GeneratedRegex(@"(?<![\w.\-@])(?<key>[A-Za-z_@][\w.\-@]{0,48})[""']?\s*[:=]\s*[""']?(?<value>[A-Za-z0-9][\w\-.:/+]{2,127})")]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"\b00-(?<trace>[0-9a-f]{32})-(?<span>[0-9a-f]{16})-[0-9a-f]{2}\b")]
    private static partial Regex TraceparentPattern();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidPattern();

    public static IReadOnlyList<CorrelationId> Extract(string text, StructuredLogEvent? structured = null)
    {
        var found = new List<CorrelationId>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string? value)
        {
            if (found.Count < MaxIds && !string.IsNullOrWhiteSpace(value) && value.Length >= 3 && seen.Add(value))
            {
                found.Add(new CorrelationId(name, value));
            }
        }

        if (structured is not null)
        {
            Add("TraceId", structured.TraceId);
            Add("SpanId", structured.SpanId);
            foreach (var (key, value) in structured.Properties)
            {
                if (IsCorrelationKey(key))
                {
                    Add(key, value);
                }
            }
        }

        foreach (Match match in TraceparentPattern().Matches(text))
        {
            Add("TraceId", match.Groups["trace"].Value);
            Add("SpanId", match.Groups["span"].Value);
        }

        foreach (Match match in KeyValuePattern().Matches(text))
        {
            var key = match.Groups["key"].Value;
            if (IsCorrelationKey(key))
            {
                Add(key.Trim('@', '"', '\''), match.Groups["value"].Value.TrimEnd('.', ':', ',', '/'));
            }
        }

        foreach (Match match in GuidPattern().Matches(text))
        {
            Add("GUID", match.Value);
        }

        return found;
    }

    public static bool IsCorrelationKey(string key)
    {
        Span<char> buffer = stackalloc char[Math.Min(key.Length, 64)];
        var length = 0;
        foreach (var c in key)
        {
            if (length == buffer.Length)
            {
                break;
            }

            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        var normalized = buffer[..length];
        foreach (var suffix in KnownKeySuffixes)
        {
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
