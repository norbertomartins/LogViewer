using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Services;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Search;
using LogViewer.Core.Services.Diagnostics;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsStore _settingsStore;
    private readonly IDialogService _dialogService;
    private readonly IFullTextSearchService _fileSearchService;
    private readonly IEventLogSearchService _eventLogSearchService;
    private readonly ThemeService _themeService;
    private readonly AppSettings _settings;
    private readonly ProcessStatsService _processStats = new();
    private readonly DispatcherTimer _statsTimer;
    private ThemeBaseMode _currentThemeMode;

    [ObservableProperty]
    private TailDocumentViewModel? _activeDocument;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _windowTitle = "LogViewer";

    public MainViewModel(
        ISettingsStore settingsStore,
        AppSettings settings,
        IDialogService dialogService,
        DockingWindowModeHost host,
        IFullTextSearchService fileSearchService,
        IEventLogSearchService eventLogSearchService,
        ThemeService themeService)
    {
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _fileSearchService = fileSearchService;
        _eventLogSearchService = eventLogSearchService;
        _themeService = themeService;
        Host = host;

        _settings = settings;
        Host.Mode = _settings.Layout.LastWindowMode;

        _currentThemeMode = _themeService.ResolveActiveTheme(_settings).BaseMode;
        _themeService.ThemeApplied += OnThemeApplied;

        if (_settings.RestorePreviousSessionOnStartup)
        {
            RestoreSession();
        }

        _statsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += (_, _) => UpdateWindowTitle();
        _statsTimer.Start();
        UpdateWindowTitle();

        RefreshHighlightPresetToggles();
    }

    private void OnThemeApplied()
    {
        _currentThemeMode = _themeService.ResolveActiveTheme(_settings).BaseMode;
        foreach (var document in Documents)
        {
            document.ApplyThemeMode(_currentThemeMode);
        }
    }

    private void UpdateWindowTitle()
    {
        var totalLines = Documents.Sum(d => d.TotalLinesAppended);
        var snapshot = _processStats.Sample(totalLines);
        WindowTitle = $"LogViewer — RAM: {snapshot.WorkingSetMb:F0} MB | CPU: {snapshot.CpuPercent:F1}% | {snapshot.LinesPerSecond:F0} lines/sec";
    }

    public DockingWindowModeHost Host { get; }

    public ObservableCollection<TailDocumentViewModel> Documents => Host.Documents;

    /// <summary>The "Recent Files" menu only ever showed plain files — <see cref="RecentSources"/> restore-tracks
    /// all three source kinds, so this filters back down to what that menu should display.</summary>
    public IReadOnlyList<TailSourceSettings> RecentFiles => _settings.RecentSources.Where(r => r.Kind == TailSourceKind.File).ToList();

    /// <summary>Exposed read-only so <c>MainWindow</c> can apply the saved window bounds/docking layout on load.</summary>
    public WindowLayoutSettings WindowLayout => _settings.Layout;

    [RelayCommand]
    private void OpenFile()
    {
        var paths = _dialogService.ShowOpenFileDialog();
        if (paths is null)
        {
            return;
        }

        foreach (var path in paths)
        {
            OpenPath(path);
        }
    }

    public TailDocumentViewModel OpenPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!TryActivateExisting(fullPath, out var document))
        {
            var options = new TailSourceOptions { PollInterval = TimeSpan.FromMilliseconds(250) };
            var source = new FileTailSource(fullPath, options);
            var isStructuredView = FindExistingOverride(TailSourceKind.File, fullPath, pattern: null, eventLogChannel: null)
                ?? SerilogFormatDetector.SniffFile(fullPath);
            document = AddDocument(source, fullPath, isStructuredView: isStructuredView);
        }

        RecordRecent(new TailSourceSettings { Kind = TailSourceKind.File, Path = fullPath });
        return document!;
    }

    /// <summary>Looks up whether a previously-saved recent-source entry for this dedup key has an explicit
    /// (non-auto) <see cref="TailSourceSettings.IsStructuredView"/> choice, before <see cref="RecordRecent"/>
    /// discards that entry in favor of a fresh one synced from the live document at save time.</summary>
    private bool? FindExistingOverride(TailSourceKind kind, string path, string? pattern, string? eventLogChannel)
    {
        var dedupKey = ComputeDedupKey(kind, path, pattern, eventLogChannel);
        return _settings.RecentSources
            .FirstOrDefault(r => string.Equals(ComputeDedupKey(r), dedupKey, StringComparison.OrdinalIgnoreCase))
            ?.IsStructuredView;
    }

    [RelayCommand]
    private void OpenRecentFile(string path) => OpenPath(path);

    [RelayCommand]
    private void OpenDirectoryWatch() => PromptOpenDirectoryWatch(initialDirectoryPath: null);

    /// <summary>Prompts for a directory-watch pattern, pre-filling the directory (e.g. from a drag-drop) when given.</summary>
    public void PromptOpenDirectoryWatch(string? initialDirectoryPath)
    {
        var selection = _dialogService.ShowOpenDirectoryWatchDialog(initialDirectoryPath);
        if (selection is null)
        {
            return;
        }

        OpenDirectoryWatch(selection.DirectoryPath, selection.Pattern, selection.AutoSwitchToLatestFile);
    }

    public TailDocumentViewModel OpenDirectoryWatch(string directoryPath, string pattern, bool autoSwitchToLatestFile)
    {
        var fullDirectory = Path.GetFullPath(directoryPath);
        var dedupKey = ComputeDedupKey(TailSourceKind.DirectoryWatch, fullDirectory, pattern, null);

        if (!TryActivateExisting(dedupKey, out var document))
        {
            var options = new TailSourceOptions { PollInterval = TimeSpan.FromMilliseconds(250) };
            var source = new DirectoryWatchTailSource(fullDirectory, pattern, autoSwitchToLatestFile, options);
            var title = $"{pattern} ({Path.GetFileName(fullDirectory.TrimEnd(Path.DirectorySeparatorChar))})";
            document = AddDocument(source, dedupKey, title);

            // Start() (inside AddDocument -> the TailDocumentViewModel ctor) resolves the initial active file,
            // so ActiveFilePath is only known after construction — sniff it now, before any lines are flushed.
            var overrideValue = FindExistingOverride(TailSourceKind.DirectoryWatch, fullDirectory, pattern, eventLogChannel: null);
            document.IsStructuredView = overrideValue
                ?? (source.ActiveFilePath is { } activeFile && SerilogFormatDetector.SniffFile(activeFile));
        }

        RecordRecent(new TailSourceSettings
        {
            Kind = TailSourceKind.DirectoryWatch,
            Path = fullDirectory,
            WildcardPattern = pattern,
            AutoSwitchToLatestFile = autoSwitchToLatestFile,
        });
        return document!;
    }

    [RelayCommand]
    private void OpenEventLog()
    {
        var selection = _dialogService.ShowOpenEventLogDialog();
        if (selection is null)
        {
            return;
        }

        OpenEventLog(selection.ChannelName, selection.Filters);
    }

    public TailDocumentViewModel OpenEventLog(string channelName, IReadOnlyList<EventLogFilterRule> filters)
    {
        var dedupKey = ComputeDedupKey(TailSourceKind.EventLog, string.Empty, null, channelName);

        if (!TryActivateExisting(dedupKey, out var document))
        {
            var source = new WindowsEventLogSource(channelName, filters);
            document = AddDocument(source, dedupKey, $"[EventLog] {channelName}", channelName, filters);
        }

        RecordRecent(new TailSourceSettings
        {
            Kind = TailSourceKind.EventLog,
            EventLogChannelName = channelName,
            EventLogFilters = filters.ToList(),
        });
        return document!;
    }

    [RelayCommand]
    private void OpenServices() => _dialogService.ShowServicesDialog();

    private bool TryActivateExisting(string dedupKey, out TailDocumentViewModel? existing)
    {
        existing = Documents.FirstOrDefault(d => string.Equals(d.SourcePath, dedupKey, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return false;
        }

        ActiveDocument = existing;
        return true;
    }

    private TailDocumentViewModel AddDocument(
        ITailSource source,
        string dedupKey,
        string? title = null,
        string? eventLogChannelName = null,
        IReadOnlyList<EventLogFilterRule>? eventLogFilters = null,
        bool isStructuredView = false)
    {
        var document = new TailDocumentViewModel(
            source,
            dedupKey,
            _settings.HighlightPresets,
            _settings.ExternalTools,
            _settings.RingBufferCapacity,
            EffectiveUiRefreshInterval(),
            title,
            eventLogChannelName,
            eventLogFilters,
            isStructuredView);
        document.SetInitialMdiBounds(Documents.Count);
        document.ApplyThemeMode(_currentThemeMode);
        document.ApplyColorizeStructuredValues(_settings.ColorizeStructuredValues);
        document.SearchRequested += () => ShowSearchDialog(document);
        document.CustomizeRequested += () => ShowCustomizeDialog(document);

        Documents.Add(document);
        ActiveDocument = document;
        return document;
    }

    private TimeSpan EffectiveUiRefreshInterval()
    {
        var configured = TimeSpan.FromMilliseconds(_settings.UiRefreshIntervalMs);
        var isRemote = _settings.AutoTuneForRemoteDesktop && RemoteSessionDetector.IsRemoteSession;
        return RemoteSessionDetector.EffectiveRefreshInterval(configured, isRemote);
    }

    private void ShowSearchDialog(TailDocumentViewModel document) =>
        _dialogService.ShowSearchDialog(document, _fileSearchService, _eventLogSearchService);

    private void ShowCustomizeDialog(TailDocumentViewModel document) => _dialogService.ShowCustomizeDialog(document);

    [RelayCommand]
    private void CloseDocument(TailDocumentViewModel? document)
    {
        document ??= ActiveDocument;
        if (document is null)
        {
            return;
        }

        Documents.Remove(document);
        document.Dispose();

        if (ReferenceEquals(ActiveDocument, document))
        {
            ActiveDocument = Documents.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void SwitchWindowMode(string modeName)
    {
        if (!Enum.TryParse<WindowModeKind>(modeName, out var mode))
        {
            return;
        }

        try
        {
            Host.SwitchMode(mode);
        }
        catch (NotSupportedException ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void EditHighlightPresets()
    {
        if (_dialogService.ShowHighlightPresetEditor(_settings.HighlightPresets))
        {
            ApplyHighlightPresetsToAllDocuments();
            RefreshHighlightPresetToggles();
        }
    }

    private void ApplyHighlightPresetsToAllDocuments()
    {
        foreach (var document in Documents)
        {
            document.ApplyHighlightPresets(_settings.HighlightPresets);
        }
    }

    /// <summary>Backs the toolbar's quick-toggle submenu — one entry per preset, kept in sync with
    /// <see cref="AppSettings.HighlightPresets"/> whenever the full preset editor is used to add/remove/rename presets.</summary>
    public ObservableCollection<HighlightPresetToggleViewModel> HighlightPresetToggles { get; } = [];

    private void RefreshHighlightPresetToggles()
    {
        foreach (var toggle in HighlightPresetToggles)
        {
            toggle.EnabledChanged -= OnHighlightPresetToggleChanged;
        }

        HighlightPresetToggles.Clear();

        foreach (var preset in _settings.HighlightPresets)
        {
            var toggle = new HighlightPresetToggleViewModel(preset.Id, preset.Name, preset.IsEnabled);
            toggle.EnabledChanged += OnHighlightPresetToggleChanged;
            HighlightPresetToggles.Add(toggle);
        }
    }

    private void OnHighlightPresetToggleChanged(HighlightPresetToggleViewModel toggle, bool isEnabled)
    {
        var preset = _settings.HighlightPresets.FirstOrDefault(p => p.Id == toggle.Id);
        if (preset is null)
        {
            return;
        }

        preset.IsEnabled = isEnabled;
        ApplyHighlightPresetsToAllDocuments();
    }

    [RelayCommand]
    private void EditExternalTools()
    {
        var allRules = _settings.HighlightPresets.SelectMany(p => p.Rules).ToList();
        if (_dialogService.ShowExternalToolEditor(_settings.ExternalTools, allRules))
        {
            foreach (var document in Documents)
            {
                document.ApplyExternalTools(_settings.ExternalTools);
            }

            ExternalToolsChanged?.Invoke();
        }
    }

    /// <summary>Raised after the external-tool set changes so <c>MainWindow</c> can rebuild shortcut-gesture bindings.</summary>
    public event Action? ExternalToolsChanged;

    [RelayCommand]
    private void OpenSettings()
    {
        if (!_dialogService.ShowSettings(_settings))
        {
            return;
        }

        // Propagate the colorize-values setting to all currently open documents so the change
        // takes effect immediately without needing to reopen them.
        foreach (var document in Documents)
        {
            document.ApplyColorizeStructuredValues(_settings.ColorizeStructuredValues);
        }
    }

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current?.Shutdown();

    /// <summary>Captures the main window's chrome (bounds/maximize/AvalonDock layout) before it closes,
    /// so the next launch can restore it. Called from <c>MainWindow</c>'s Closing handler.</summary>
    public void CaptureWindowLayout(double left, double top, double width, double height, bool isMaximized, string? dockingLayoutXml)
    {
        _settings.Layout.WindowLeft = left;
        _settings.Layout.WindowTop = top;
        _settings.Layout.WindowWidth = width;
        _settings.Layout.WindowHeight = height;
        _settings.Layout.IsMaximized = isMaximized;
        _settings.Layout.DockingLayoutXml = dockingLayoutXml;
    }

    public void SaveAndDispose()
    {
        _settings.Layout.LastWindowMode = Host.Mode;
        _settings.Layout.ActiveSourceDedupKey = ActiveDocument?.SourcePath;

        foreach (var document in Documents)
        {
            var entry = _settings.RecentSources.FirstOrDefault(r =>
                string.Equals(ComputeDedupKey(r), document.SourcePath, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                continue;
            }

            entry.CustomColorHex = document.CustomColorHex;
            entry.CustomIconGlyph = document.CustomIconGlyph;
            entry.MdiLeft = document.MdiLeft;
            entry.MdiTop = document.MdiTop;
            entry.MdiWidth = document.MdiWidth;
            entry.MdiHeight = document.MdiHeight;
            entry.MdiIsMaximized = document.IsMdiMaximized;
            entry.IsStructuredView = document.IsStructuredView;
        }

        _settingsStore.Save(_settings);
        Dispose();
    }

    public void Dispose()
    {
        _themeService.ThemeApplied -= OnThemeApplied;
        _statsTimer.Stop();
        _processStats.Dispose();

        foreach (var document in Documents)
        {
            document.Dispose();
        }
    }

    // --- Session restore -------------------------------------------------------------------

    private void RestoreSession()
    {
        foreach (var entry in _settings.RecentSources.ToList())
        {
            var document = entry.Kind switch
            {
                TailSourceKind.File when File.Exists(entry.Path) => OpenPath(entry.Path),
                TailSourceKind.DirectoryWatch when Directory.Exists(entry.Path) =>
                    OpenDirectoryWatch(entry.Path, entry.WildcardPattern ?? "*.log", entry.AutoSwitchToLatestFile),
                TailSourceKind.EventLog when entry.EventLogChannelName is not null =>
                    OpenEventLog(entry.EventLogChannelName, entry.EventLogFilters),
                _ => null,
            };

            if (document is not null)
            {
                ApplyRestoredCustomization(document, entry);
            }
        }

        if (_settings.Layout.ActiveSourceDedupKey is { } activeKey)
        {
            var active = Documents.FirstOrDefault(d => string.Equals(d.SourcePath, activeKey, StringComparison.OrdinalIgnoreCase));
            if (active is not null)
            {
                ActiveDocument = active;
            }
        }
    }

    private static void ApplyRestoredCustomization(TailDocumentViewModel document, TailSourceSettings entry)
    {
        if (entry.CustomColorHex is not null)
        {
            document.CustomColorHex = entry.CustomColorHex;
        }

        if (entry.CustomIconGlyph is not null)
        {
            document.CustomIconGlyph = entry.CustomIconGlyph;
        }

        if (entry.MdiLeft is { } left && entry.MdiTop is { } top && entry.MdiWidth is { } width && entry.MdiHeight is { } height)
        {
            document.MdiLeft = left;
            document.MdiTop = top;
            document.MdiWidth = width;
            document.MdiHeight = height;
            document.IsMdiMaximized = entry.MdiIsMaximized;
        }
    }

    private void RecordRecent(TailSourceSettings entry)
    {
        var dedupKey = ComputeDedupKey(entry);
        _settings.RecentSources.RemoveAll(r => string.Equals(ComputeDedupKey(r), dedupKey, StringComparison.OrdinalIgnoreCase));
        _settings.RecentSources.Insert(0, entry);
        if (_settings.RecentSources.Count > 10)
        {
            _settings.RecentSources.RemoveRange(10, _settings.RecentSources.Count - 10);
        }

        OnPropertyChanged(nameof(RecentFiles));
    }

    private static string ComputeDedupKey(TailSourceSettings entry) =>
        ComputeDedupKey(entry.Kind, entry.Path, entry.WildcardPattern, entry.EventLogChannelName);

    private static string ComputeDedupKey(TailSourceKind kind, string path, string? pattern, string? eventLogChannel) => kind switch
    {
        TailSourceKind.File => Path.GetFullPath(path),
        TailSourceKind.DirectoryWatch => $"dirwatch:{Path.GetFullPath(path)}|{pattern}",
        TailSourceKind.EventLog => $"eventlog:{eventLogChannel}",
        _ => path,
    };
}
