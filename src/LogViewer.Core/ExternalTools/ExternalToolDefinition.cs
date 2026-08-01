namespace LogViewer.Core.ExternalTools;

/// <summary>
/// A user-configured external tool, launched via <see cref="ExternalToolLauncher"/> against the active
/// document — manually (toolbar/shortcut) or automatically when <see cref="AutoTriggerOnHighlightMatch"/>
/// is set and a line matches <see cref="TriggerHighlightRuleId"/>.
/// </summary>
public sealed record ExternalToolDefinition(
    Guid Id,
    string Name,
    string ExecutablePath,
    string ArgumentTemplate,
    string? ShortcutGesture,
    bool AutoTriggerOnHighlightMatch,
    Guid? TriggerHighlightRuleId)
{
    public static ExternalToolDefinition CreateDefault(string name) => new(
        Id: Guid.NewGuid(),
        Name: name,
        ExecutablePath: string.Empty,
        ArgumentTemplate: "{FilePath}",
        ShortcutGesture: null,
        AutoTriggerOnHighlightMatch: false,
        TriggerHighlightRuleId: null);
}
