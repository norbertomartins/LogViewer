using LogViewer.App.Services;
using LogViewer.App.ViewModels;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Search;
using NSubstitute;

namespace LogViewer.App.Tests.TestUtilities;

/// <summary>Builds a <see cref="MainViewModel"/> with a fresh in-memory <see cref="AppSettings"/> and
/// substitute collaborators, so tests can construct one without touching disk or real dialogs.</summary>
public static class MainViewModelFactory
{
    public static (MainViewModel ViewModel, AppSettings Settings) Create(
        AppSettings? settings = null,
        IDialogService? dialogService = null)
    {
        var usedSettings = settings ?? new AppSettings { RestorePreviousSessionOnStartup = false };

        var settingsStore = Substitute.For<ISettingsStore>();
        var dialogs = dialogService ?? Substitute.For<IDialogService>();
        var host = new DockingWindowModeHost();
        var themeService = new ThemeService();

        var viewModel = new MainViewModel(
            settingsStore,
            usedSettings,
            dialogs,
            host,
            Substitute.For<IFullTextSearchService>(),
            Substitute.For<IEventLogSearchService>(),
            Substitute.For<ISimilarBlockFinder>(),
            themeService);

        return (viewModel, usedSettings);
    }
}
