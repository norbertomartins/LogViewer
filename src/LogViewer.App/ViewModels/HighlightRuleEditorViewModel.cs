using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.ViewModels;

public sealed partial class HighlightRuleEditorViewModel : ObservableObject
{
    [ObservableProperty]
    private HighlightRuleViewModel? _selectedRule;

    public HighlightRuleEditorViewModel(IEnumerable<HighlightRule> rules)
    {
        Rules = new ObservableCollection<HighlightRuleViewModel>(rules.Select(r => new HighlightRuleViewModel(r)));
        SelectedRule = Rules.FirstOrDefault();
    }

    public ObservableCollection<HighlightRuleViewModel> Rules { get; }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new HighlightRuleViewModel();
        Rules.Add(rule);
        SelectedRule = rule;
    }

    [RelayCommand]
    private void RemoveRule(HighlightRuleViewModel? rule)
    {
        rule ??= SelectedRule;
        if (rule is null)
        {
            return;
        }

        Rules.Remove(rule);
        SelectedRule = Rules.FirstOrDefault();
    }

    public IReadOnlyList<HighlightRule> ToRules() => Rules.Select(r => r.ToRule()).ToList();
}
