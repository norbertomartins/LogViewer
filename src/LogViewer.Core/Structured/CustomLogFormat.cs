namespace LogViewer.Core.Structured;

/// <summary>
/// A user-defined line format: a .NET regular expression whose <b>named groups</b> map onto a
/// <see cref="StructuredLogEvent"/>, persisted in <c>AppSettings.CustomLogFormats</c> and parsed by
/// <see cref="RegexLogLineParser"/>. Recognized group names (case-insensitive):
/// <list type="bullet">
/// <item><c>timestamp</c> / <c>ts</c> / <c>time</c> / <c>datetime</c> — or separate <c>date</c> + <c>time</c> groups;</item>
/// <item><c>level</c> / <c>lvl</c> / <c>severity</c> / <c>loglevel</c>;</item>
/// <item><c>message</c> / <c>msg</c> (the whole line when absent);</item>
/// <item><c>exception</c> / <c>error</c> / <c>stacktrace</c>, <c>traceid</c>, <c>spanid</c>, <c>template</c>.</item>
/// </list>
/// Every other named group becomes a structured property (filterable, shown in the detail panel).
/// </summary>
public sealed class CustomLogFormat
{
    public const string IdPrefix = "custom-";

    /// <summary>Stable id persisted as a document's <c>StructuredFormatId</c>; always starts with <see cref="IdPrefix"/>.</summary>
    public string Id { get; set; } = IdPrefix + Guid.NewGuid().ToString("N")[..8];

    public string Name { get; set; } = string.Empty;

    public string Pattern { get; set; } = string.Empty;

    public bool IgnoreCase { get; set; }

    /// <summary>Optional exact .NET date/time format(s) for the timestamp group, separated by <c>|</c>
    /// (e.g. <c>dd/MM/yyyy HH:mm:ss.fff</c>). When empty, common ISO-like shapes are recognized automatically.</summary>
    public string? TimestampFormat { get; set; }

    /// <summary>When a parsed timestamp carries no offset: true reads it as UTC, false (default) as the local
    /// time zone — most hand-rolled text logs are written in the server's local time.</summary>
    public bool TimestampIsUtc { get; set; }

    public CustomLogFormat Clone() => (CustomLogFormat)MemberwiseClone();
}
