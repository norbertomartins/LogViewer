using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.ViewModels;

/// <summary>Editable wrapper around a <see cref="HighlightPreset"/> for the preset editor dialog: its own
/// name/enabled state plus the ordered list of rules it contains, with add/remove/reorder commands.</summary>
public sealed partial class HighlightPresetViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isEnabled;

    [NotifyCanExecuteChangedFor(nameof(RemoveRuleCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRuleUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MoveRuleDownCommand))]
    [ObservableProperty]
    private HighlightRuleViewModel? _selectedRule;

    public HighlightPresetViewModel()
        : this(HighlightPreset.CreateDefault("New Preset"))
    {
    }

    public HighlightPresetViewModel(HighlightPreset preset)
    {
        Id = preset.Id;
        _name = preset.Name;
        _isEnabled = preset.IsEnabled;
        Rules = new ObservableCollection<HighlightRuleViewModel>(preset.Rules.Select(r => new HighlightRuleViewModel(r)));
        SelectedRule = Rules.FirstOrDefault();
    }

    public Guid Id { get; }

    public ObservableCollection<HighlightRuleViewModel> Rules { get; }

    [RelayCommand]
    private void AddRule()
    {
        var rule = new HighlightRuleViewModel();
        Rules.Add(rule);
        SelectedRule = rule;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void RemoveRule(HighlightRuleViewModel? rule)
    {
        rule ??= SelectedRule;
        if (rule is null)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        Rules.Remove(rule);
        SelectedRule = Rules.ElementAtOrDefault(Math.Min(index, Rules.Count - 1));
    }

    [RelayCommand(CanExecute = nameof(CanMoveRuleUp))]
    private void MoveRuleUp() => MoveSelectedRule(-1);

    [RelayCommand(CanExecute = nameof(CanMoveRuleDown))]
    private void MoveRuleDown() => MoveSelectedRule(1);

    private bool HasSelectedRule() => SelectedRule is not null;

    private bool CanMoveRuleUp() => SelectedRule is not null && Rules.IndexOf(SelectedRule) > 0;

    private bool CanMoveRuleDown() => SelectedRule is not null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;

    private void MoveSelectedRule(int offset)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = Rules.IndexOf(SelectedRule);
        Rules.Move(index, index + offset);
    }

    public HighlightPreset ToPreset() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        Rules = Rules.Select(r => r.ToRule()).ToList(),
    };
}
