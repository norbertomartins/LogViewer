using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Services;
using LogViewer.Core.Configuration;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private WindowModeKind _defaultWindowMode;

    [ObservableProperty]
    private int _ringBufferCapacity;

    [ObservableProperty]
    private int _uiRefreshIntervalMs;

    [ObservableProperty]
    private bool _restorePreviousSessionOnStartup;

    [ObservableProperty]
    private bool _autoTuneForRemoteDesktop;

    [ObservableProperty]
    private bool _colorizeStructuredValues;

    [ObservableProperty]
    private double _logFontSize;

    [ObservableProperty]
    private IReadOnlyList<AppTheme> _availableThemes;

    [ObservableProperty]
    private AppTheme _selectedTheme;

    public SettingsViewModel(AppSettings settings, IDialogService dialogService)
    {
        _settings = settings;
        _dialogService = dialogService;

        _defaultWindowMode = settings.DefaultWindowMode;
        _ringBufferCapacity = settings.RingBufferCapacity;
        _uiRefreshIntervalMs = settings.UiRefreshIntervalMs;
        _restorePreviousSessionOnStartup = settings.RestorePreviousSessionOnStartup;
        _autoTuneForRemoteDesktop = settings.AutoTuneForRemoteDesktop;
        _colorizeStructuredValues = settings.ColorizeStructuredValues;
        _logFontSize = settings.LogFontSize;

        _availableThemes = BuildAvailableThemes();
        _selectedTheme = _availableThemes.FirstOrDefault(t => t.Id == settings.ActiveThemeId) ?? _availableThemes[0];
    }

    public IReadOnlyList<WindowModeKind> AvailableWindowModes { get; } = [WindowModeKind.Tabbed, WindowModeKind.Floating];

    [RelayCommand]
    private void ManageThemes()
    {
        if (!_dialogService.ShowThemeManager(_settings))
        {
            return;
        }

        AvailableThemes = BuildAvailableThemes();
        SelectedTheme = AvailableThemes.FirstOrDefault(t => t.Id == _settings.ActiveThemeId) ?? AvailableThemes[0];
    }

    private IReadOnlyList<AppTheme> BuildAvailableThemes() => [.. BuiltInThemes.All, .. _settings.CustomThemes];

    public void ApplyTo(AppSettings settings)
    {
        settings.DefaultWindowMode = DefaultWindowMode;
        settings.RingBufferCapacity = RingBufferCapacity;
        settings.UiRefreshIntervalMs = UiRefreshIntervalMs;
        settings.RestorePreviousSessionOnStartup = RestorePreviousSessionOnStartup;
        settings.AutoTuneForRemoteDesktop = AutoTuneForRemoteDesktop;
        settings.ColorizeStructuredValues = ColorizeStructuredValues;
        settings.LogFontSize = LogFontSize;
        settings.ActiveThemeId = SelectedTheme.Id;
    }
}
