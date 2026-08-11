using System.Text.RegularExpressions;
using LogViewer.Core.Structured;

namespace LogViewer.Core.BlockDiff;

/// <summary>
/// Normalizes a <see cref="StructuredLogEvent"/> into a variable-agnostic "shape" signature so two
/// renders of the same log statement (different duration/id/status values) collapse to the same
/// signature, letting <see cref="LogBlockExtractor"/>/<see cref="BlockAlignment"/> recognize "the same
/// kind of log line" across two files.
/// </summary>
public static class MessageSignature
{
    // Unit Separator (0x1F) -- never appears in normal log text, so it is a safe field delimiter for
    // the composite signature string without risking accidental collisions with real message content.
    private const char FieldSeparator = (char)0x1F;

    // Applied in this order: more specific patterns must consume their text before the generic
    // number pattern runs, or e.g. a GUID's hex/digit runs would get fragmented by it first.
    private static readonly Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);

    private static readonly Regex IpPattern = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);

    private static readonly Regex DateTimePattern = new(
        @"\b\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?\b", RegexOptions.Compiled);

    private static readonly Regex TimePattern = new(@"\b\d{1,2}:\d{2}:\d{2}(\.\d+)?\b", RegexOptions.Compiled);

    private static readonly Regex QuotedStringPattern = new(
        @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'", RegexOptions.Compiled);

    private static readonly Regex HexPattern = new(@"\b0x[0-9a-fA-F]+\b", RegexOptions.Compiled);

    private static readonly Regex LongIdPattern = new(@"\b(?=[0-9A-Za-z]*\d)[0-9A-Za-z]{8,}\b", RegexOptions.Compiled);

    // No trailing \b: a duration/count is often immediately followed by a unit suffix with no
    // separator ("120ms", "42%"), where \d and the following letter are both word characters and
    // therefore share no boundary — requiring one there would leave the digits unmasked.
    private static readonly Regex NumberPattern = new(@"-?\b\d+(\.\d+)?", RegexOptions.Compiled);

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Computes the block-comparison signature for one event: the raw <see cref="StructuredLogEvent.MessageTemplate"/>
    /// when present (already variable-agnostic), or a masked/normalized form of <see cref="StructuredLogEvent.RenderedMessage"/>
    /// plus the sorted set of property *keys* (not values) when there's no template -- the key set guards against two
    /// structurally different call sites' masked text accidentally colliding.</summary>
    public static string Compute(StructuredLogEvent evt)
    {
        var level = evt.Level ?? string.Empty;

        if (!string.IsNullOrEmpty(evt.MessageTemplate))
        {
            return $"{level}{FieldSeparator}{evt.MessageTemplate}";
        }

        var masked = Mask(evt.RenderedMessage);
        var keys = string.Join(",", evt.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal));
        return $"{level}{FieldSeparator}{masked}{FieldSeparator}{keys}";
    }

    /// <summary>Replaces dynamic tokens (GUIDs, IPs, dates/times, quoted strings, hex, long alphanumeric ids,
    /// numbers) with bracketed placeholders, exposed separately so the regex set can be unit-tested directly.</summary>
    public static string Mask(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = GuidPattern.Replace(text, "<guid>");
        result = IpPattern.Replace(result, "<ip>");
        result = DateTimePattern.Replace(result, "<datetime>");
        result = TimePattern.Replace(result, "<time>");
        result = QuotedStringPattern.Replace(result, "<str>");
        result = HexPattern.Replace(result, "<hex>");
        result = LongIdPattern.Replace(result, "<id>");
        result = NumberPattern.Replace(result, "<num>");
        return WhitespacePattern.Replace(result, " ").Trim();
    }
}
