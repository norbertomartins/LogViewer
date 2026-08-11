using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LogViewer.App.Services;
using LogViewer.App.ViewModels;
using LogViewer.App.Views.Shell;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Search;
using Microsoft.Extensions.DependencyInjection;

namespace LogViewer.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every window (MainWindow and every dialog, present and future) gets its title bar dark-mode
        // flag set as soon as it loads, matching whatever theme is active at that moment — this is what
        // covers dialogs opened after a theme switch, not just the ones open when Apply() last ran.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAnyWindowLoaded));

        // AvalonDock document tab headers are plain TabItems styled by its GenericTheme (see MainWindow.xaml),
        // which isn't aware of our dynamic dark/light brushes — its unselected-tab foreground stays a fixed
        // dark color, unreadable once MainWindow forces the tab's own background dark too (only the currently
        // *selected* tab happened to come out legible). A class handler reaches every TabItem app-wide,
        // including ones inside AvalonDock's separate floating-window Windows that MainWindow's own visual
        // tree walk (SyncActiveDocumentTabBackground) can't see, and re-fires on every selection change.
        EventManager.RegisterClassHandler(typeof(TabItem), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnAnyDocumentTabItemStyled));
        EventManager.RegisterClassHandler(typeof(TabItem), Selector.SelectedEvent, new RoutedEventHandler(OnAnyDocumentTabItemStyled));
        EventManager.RegisterClassHandler(typeof(TabItem), Selector.UnselectedEvent, new RoutedEventHandler(OnAnyDocumentTabItemStyled));

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore>(_ => JsonSettingsStore.CreateDefault());
        services.AddSingleton(sp => sp.GetRequiredService<ISettingsStore>().Load());
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFullTextSearchService, FileFullTextSearchService>();
        services.AddSingleton<IEventLogSearchService, EventLogSearchService>();
        services.AddSingleton<IBlockScanService, FileBlockScanService>();
        services.AddSingleton<ISimilarBlockFinder, SimilarBlockFinder>();
        services.AddSingleton<DockingWindowModeHost>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var settings = _serviceProvider.GetRequiredService<AppSettings>();
        var themeService = _serviceProvider.GetRequiredService<ThemeService>();
        themeService.Apply(themeService.ResolveActiveTheme(settings));

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.DataContext = _serviceProvider.GetRequiredService<MainViewModel>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.GetService<MainViewModel>()?.SaveAndDispose();
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void OnAnyWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

#pragma warning disable WPF0001 // ThemeMode is experimental in this SDK.
        DarkTitleBar.Apply(window, Current.ThemeMode == ThemeMode.Dark);
#pragma warning restore WPF0001
    }

    private static void OnAnyDocumentTabItemStyled(object sender, RoutedEventArgs e)
    {
        if (sender is not TabItem tabItem)
        {
            return;
        }

        tabItem.SetResourceReference(Control.ForegroundProperty, "Theme.LogForeground");
        tabItem.SetResourceReference(Control.BackgroundProperty, tabItem.IsSelected ? "Theme.LogBackground" : "Theme.WorkspaceBackground");
    }
}
