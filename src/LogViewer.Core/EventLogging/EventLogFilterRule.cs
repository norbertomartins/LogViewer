namespace LogViewer.Core.EventLogging;

/// <summary>Which part of an event record a filter's regex is evaluated against.</summary>
public enum EventLogFilterField
{
    Message,
    ProviderName,
    Level,
}

/// <summary>
/// A per-source regex filter for a <see cref="WindowsEventLogSource"/>, independently toggleable.
/// Semantics: if no filter is enabled, every event passes; if at least one is enabled, an event
/// passes only if it matches at least one enabled filter (OR across filters).
/// </summary>
public sealed record EventLogFilterRule(
    Guid Id,
    string Name,
    string? ProviderName,
    string RegexPattern,
    bool IsEnabled,
    EventLogFilterField Field)
{
    public static EventLogFilterRule CreateDefault(string name, string regexPattern) => new(
        Id: Guid.NewGuid(),
        Name: name,
        ProviderName: null,
        RegexPattern: regexPattern,
        IsEnabled: true,
        Field: EventLogFilterField.Message);
}
