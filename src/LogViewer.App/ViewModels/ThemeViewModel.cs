using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

/// <summary>Editable wrapper around <see cref="AppTheme"/> for the theme manager dialog. Built-in
/// themes are shown read-only (see <see cref="IsBuiltIn"/>) — only duplicates are ever edited in place.</summary>
public sealed partial class ThemeViewModel : ObservableObject
{
    public string Id { get; }

    public bool IsBuiltIn { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isActive;

    /// <summary>Governs native chrome (menus, scrollbars, dialogs, combo boxes) via WPF's Fluent theme.</summary>
    [ObservableProperty]
    private ThemeBaseMode _baseMode;

    [ObservableProperty]
    private string _borderColorHex;

    [ObservableProperty]
    private string _workspaceBackgroundHex;

    [ObservableProperty]
    private string _logBackgroundHex;

    [ObservableProperty]
    private string _logForegroundHex;

    public ThemeViewModel(AppTheme theme)
    {
        Id = theme.Id;
        IsBuiltIn = theme.IsBuiltIn;
        _name = theme.Name;
        _baseMode = theme.BaseMode;
        _borderColorHex = theme.GetColor(ThemeColorKeys.BorderColor);
        _workspaceBackgroundHex = theme.GetColor(ThemeColorKeys.WorkspaceBackground);
        _logBackgroundHex = theme.GetColor(ThemeColorKeys.LogBackground);
        _logForegroundHex = theme.GetColor(ThemeColorKeys.LogForeground);
    }

    public AppTheme ToAppTheme() => new()
    {
        Id = Id,
        Name = Name,
        IsBuiltIn = IsBuiltIn,
        BaseMode = BaseMode,
        Colors = new Dictionary<string, string>
        {
            [ThemeColorKeys.BorderColor] = BorderColorHex,
            [ThemeColorKeys.WorkspaceBackground] = WorkspaceBackgroundHex,
            [ThemeColorKeys.LogBackground] = LogBackgroundHex,
            [ThemeColorKeys.LogForeground] = LogForegroundHex,
        },
    };
}
