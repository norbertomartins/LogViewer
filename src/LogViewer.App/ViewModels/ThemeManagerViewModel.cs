using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.Core.Configuration;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

/// <summary>Backs the theme manager dialog: list of built-in + custom themes with duplicate/new/delete
/// and per-color editing. Built-in themes stay read-only so "duplicate an existing one" always has a
/// known-good starting point.</summary>
public sealed partial class ThemeManagerViewModel : ObservableObject
{
    private string _activeThemeId;

    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateCommand))]
    [ObservableProperty]
    private ThemeViewModel? _selectedTheme;

    public ThemeManagerViewModel(AppSettings settings)
    {
        _activeThemeId = settings.ActiveThemeId;

        var all = BuiltInThemes.All.Concat(settings.CustomThemes);
        Themes = new ObservableCollection<ThemeViewModel>(all.Select(t => new ThemeViewModel(t)
        {
            IsActive = t.Id == _activeThemeId,
        }));

        SelectedTheme = Themes.FirstOrDefault(t => t.Id == _activeThemeId) ?? Themes.FirstOrDefault();
    }

    public ObservableCollection<ThemeViewModel> Themes { get; }

    [RelayCommand]
    private void NewTheme()
    {
        var basis = SelectedTheme?.ToAppTheme() ?? BuiltInThemes.Light;
        AddDuplicate(basis, "New Theme");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        if (SelectedTheme is null)
        {
            return;
        }

        AddDuplicate(SelectedTheme.ToAppTheme(), $"{SelectedTheme.Name} Copy");
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void Delete()
    {
        if (SelectedTheme is not { IsBuiltIn: false } theme)
        {
            return;
        }

        var index = Themes.IndexOf(theme);
        var wasActive = theme.Id == _activeThemeId;
        Themes.Remove(theme);

        if (wasActive)
        {
            SetActiveInternal(Themes.First(t => t.Id == BuiltInThemes.LightId));
        }

        SelectedTheme = Themes.ElementAtOrDefault(Math.Min(index, Themes.Count - 1));
    }

    private bool CanDelete() => SelectedTheme is { IsBuiltIn: false };

    private bool HasSelection() => SelectedTheme is not null;

    [RelayCommand]
    private void UseTheme(ThemeViewModel? theme)
    {
        theme ??= SelectedTheme;
        if (theme is not null)
        {
            SetActiveInternal(theme);
        }
    }

    private void SetActiveInternal(ThemeViewModel theme)
    {
        _activeThemeId = theme.Id;
        foreach (var t in Themes)
        {
            t.IsActive = t.Id == _activeThemeId;
        }
    }

    private void AddDuplicate(AppTheme basis, string nameSuffix)
    {
        var copy = basis.Duplicate(nameSuffix);
        var viewModel = new ThemeViewModel(copy);
        Themes.Add(viewModel);
        SelectedTheme = viewModel;
    }

    public IReadOnlyList<AppTheme> ToCustomThemes() => Themes.Where(t => !t.IsBuiltIn).Select(t => t.ToAppTheme()).ToList();

    public string ActiveThemeId => _activeThemeId;
}
