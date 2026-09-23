using LogViewer.Core.Tailing;

namespace LogViewer.Core.Structured;

/// <summary>
/// Last-resort parser for unstructured text logs: every non-blank line becomes an event whose level is the
/// most severe level word it contains (none → null) and whose timestamp is the leading one
/// <see cref="MergedTimestampExtractor"/> recognizes. Deliberately not registered in <see cref="LogLineParsers"/>
/// (it would "detect" every file) — <see cref="StructuredFileReader"/> falls back to it, so the pattern
/// statistics, Compare Files and the MCP analysis tools still work on a plain <c>[ERROR] …</c> log.
/// </summary>
public sealed class PlainTextLogLineParser : ILogLineParser
{
    private static readonly IReadOnlyDictionary<string, string> NoProperties = new Dictionary<string, string>();

    public string FormatId => "plaintext";

    public string DisplayName => "Plain text";

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            evt = null;
            return false;
        }

        var level = LogLevelNormalizer.GuessSeverityFromLine(line) is { } rank ? LogLevelSeverity.Levels[rank] : null;
        evt = new StructuredLogEvent(MergedTimestampExtractor.TryExtract(line), level, null, line, null, NoProperties);
        return true;
    }
}

/// <summary>Tries <paramref name="primary"/> first and hands every line it rejects to <paramref name="fallback"/>.</summary>
public sealed class FallbackLogLineParser(ILogLineParser primary, ILogLineParser fallback) : ILogLineParser
{
    public string FormatId => primary.FormatId;

    public string DisplayName => primary.DisplayName;

    public bool TryParse(string line, out StructuredLogEvent? evt) =>
        primary.TryParse(line, out evt) || fallback.TryParse(line, out evt);
}
