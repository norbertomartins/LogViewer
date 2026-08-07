namespace LogViewer.Core.Structured;

/// <summary>A single log event parsed out of a Serilog JSON line (CLEF or the standard <c>JsonFormatter</c> shape).</summary>
public sealed record StructuredLogEvent(
    DateTimeOffset? Timestamp,
    string? Level,
    string? MessageTemplate,
    string RenderedMessage,
    string? Exception,
    IReadOnlyDictionary<string, string> Properties);
