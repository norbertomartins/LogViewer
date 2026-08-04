using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.ViewModels;

/// <summary>Backs the "Manage Highlight Presets" dialog: an ordered list of presets, each with its own ordered
/// list of rules. Presets and rules are both reorderable via move-up/move-down commands since match precedence
/// on overlap is driven entirely by list position — see <see cref="HighlightPreset.FlattenForMatching"/>.</summary>
public sealed partial class HighlightPresetEditorViewModel : ObservableObject
{
    [NotifyCanExecuteChangedFor(nameof(DuplicatePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemovePresetCommand))]
    [NotifyCanExecuteChangedFor(nameof(MovePresetUpCommand))]
    [NotifyCanExecuteChangedFor(nameof(MovePresetDownCommand))]
    [ObservableProperty]
    private HighlightPresetViewModel? _selectedPreset;

    public HighlightPresetEditorViewModel(IEnumerable<HighlightPreset> presets)
    {
        Presets = new ObservableCollection<HighlightPresetViewModel>(presets.Select(p => new HighlightPresetViewModel(p)));
        SelectedPreset = Presets.FirstOrDefault();
    }

    public ObservableCollection<HighlightPresetViewModel> Presets { get; }

    [RelayCommand]
    private void AddPreset()
    {
        var preset = new HighlightPresetViewModel();
        Presets.Add(preset);
        SelectedPreset = preset;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicatePreset()
    {
        if (SelectedPreset is null)
        {
            return;
        }

        var copy = SelectedPreset.ToPreset().Duplicate($"{SelectedPreset.Name} Copy");
        var viewModel = new HighlightPresetViewModel(copy);
        Presets.Insert(Presets.IndexOf(SelectedPreset) + 1, viewModel);
        SelectedPreset = viewModel;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemovePreset(HighlightPresetViewModel? preset)
    {
        preset ??= SelectedPreset;
        if (preset is null)
        {
            return;
        }

        var index = Presets.IndexOf(preset);
        Presets.Remove(preset);
        SelectedPreset = Presets.ElementAtOrDefault(Math.Min(index, Presets.Count - 1));
    }

    [RelayCommand(CanExecute = nameof(CanMovePresetUp))]
    private void MovePresetUp() => MoveSelectedPreset(-1);

    [RelayCommand(CanExecute = nameof(CanMovePresetDown))]
    private void MovePresetDown() => MoveSelectedPreset(1);

    private bool HasSelection() => SelectedPreset is not null;

    private bool CanMovePresetUp() => SelectedPreset is not null && Presets.IndexOf(SelectedPreset) > 0;

    private bool CanMovePresetDown() => SelectedPreset is not null && Presets.IndexOf(SelectedPreset) < Presets.Count - 1;

    private void MoveSelectedPreset(int offset)
    {
        if (SelectedPreset is null)
        {
            return;
        }

        var index = Presets.IndexOf(SelectedPreset);
        Presets.Move(index, index + offset);
    }

    public IReadOnlyList<HighlightPreset> ToPresets() => Presets.Select(p => p.ToPreset()).ToList();

    public HighlightPresetExportFile ExportSelected()
    {
        var preset = SelectedPreset ?? throw new InvalidOperationException("No preset selected.");
        return new HighlightPresetExportFile(HighlightPresetExportFile.CurrentFormatVersion, [preset.ToPreset()]);
    }

    public HighlightPresetExportFile ExportAll() =>
        new(HighlightPresetExportFile.CurrentFormatVersion, ToPresets().ToList());

    public void ImportFrom(HighlightPresetExportFile file)
    {
        foreach (var preset in file.Presets)
        {
            // Fresh Id + fresh rule Ids avoid collisions with presets/rules already in this editor.
            var imported = preset.Duplicate(preset.Name);
            var viewModel = new HighlightPresetViewModel(imported);
            Presets.Add(viewModel);
            SelectedPreset = viewModel;
        }
    }
}
