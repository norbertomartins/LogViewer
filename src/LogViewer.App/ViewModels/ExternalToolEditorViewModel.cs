using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.ViewModels;

public sealed record HighlightRuleOption(Guid Id, string Name);

public sealed partial class ExternalToolEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private ExternalToolViewModel? _selectedTool;

    public ExternalToolEditorViewModel(IEnumerable<ExternalToolDefinition> tools, IReadOnlyList<HighlightRule> availableHighlightRules)
    {
        Tools = new ObservableCollection<ExternalToolViewModel>(tools.Select(t => new ExternalToolViewModel(t)));
        SelectedTool = Tools.FirstOrDefault();
        AvailableHighlightRules = availableHighlightRules.Select(r => new HighlightRuleOption(r.Id, r.Name)).ToList();
    }

    public ObservableCollection<ExternalToolViewModel> Tools { get; }

    public IReadOnlyList<HighlightRuleOption> AvailableHighlightRules { get; }

    [RelayCommand]
    private void AddTool()
    {
        var tool = new ExternalToolViewModel();
        Tools.Add(tool);
        SelectedTool = tool;
    }

    [RelayCommand]
    private void RemoveTool(ExternalToolViewModel? tool)
    {
        tool ??= SelectedTool;
        if (tool is null)
        {
            return;
        }

        Tools.Remove(tool);
        SelectedTool = Tools.FirstOrDefault();
    }

    public IReadOnlyList<ExternalToolDefinition> ToDefinitions() => Tools.Select(t => t.ToDefinition()).ToList();
}
