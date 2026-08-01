using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.Core.ExternalTools;

namespace LogViewer.App.ViewModels;

/// <summary>Editable wrapper around an <see cref="ExternalToolDefinition"/> for the tool-editor dialog.</summary>
public sealed partial class ExternalToolViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _executablePath;

    [ObservableProperty]
    private string _argumentTemplate;

    [ObservableProperty]
    private string? _shortcutGesture;

    [ObservableProperty]
    private bool _autoTriggerOnHighlightMatch;

    [ObservableProperty]
    private Guid? _triggerHighlightRuleId;

    public ExternalToolViewModel()
        : this(ExternalToolDefinition.CreateDefault("New Tool"))
    {
    }

    public ExternalToolViewModel(ExternalToolDefinition tool)
    {
        Id = tool.Id;
        _name = tool.Name;
        _executablePath = tool.ExecutablePath;
        _argumentTemplate = tool.ArgumentTemplate;
        _shortcutGesture = tool.ShortcutGesture;
        _autoTriggerOnHighlightMatch = tool.AutoTriggerOnHighlightMatch;
        _triggerHighlightRuleId = tool.TriggerHighlightRuleId;
    }

    public Guid Id { get; }

    public ExternalToolDefinition ToDefinition() => new(
        Id, Name, ExecutablePath, ArgumentTemplate, string.IsNullOrWhiteSpace(ShortcutGesture) ? null : ShortcutGesture,
        AutoTriggerOnHighlightMatch, AutoTriggerOnHighlightMatch ? TriggerHighlightRuleId : null);
}
