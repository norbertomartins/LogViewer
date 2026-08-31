namespace LogViewer.Core.Structured;

/// <summary>
/// Parses a single raw log line into a <see cref="StructuredLogEvent"/> for a specific on-disk format
/// (Serilog/CLEF, logfmt, generic NDJSON, RFC 5424/3164 syslog, W3C extended / IIS …).
/// <para>Instances are <b>not</b> required to be thread-safe or stateless — one instance is created per
/// document and fed that document's lines in order, so a parser may carry state across calls (e.g. the
/// W3C parser remembering the last <c>#Fields:</c> directive). Callers that need a stateless check
/// should use <see cref="LogLineParsers"/>' sample-based detection instead.</para>
/// </summary>
public interface ILogLineParser
{
    /// <summary>Stable identifier persisted in settings, e.g. <c>"serilog"</c>, <c>"logfmt"</c>.</summary>
    string FormatId { get; }

    /// <summary>Human-readable name shown in the "structured format" picker.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Attempts to parse <paramref name="line"/>. Returns false (with <paramref name="evt"/> null) when the
    /// line does not belong to this format. A directive/header line the parser consumes for state but that
    /// carries no event (e.g. a W3C <c>#Fields:</c> line) also returns false.
    /// </summary>
    bool TryParse(string line, out StructuredLogEvent? evt);
}
