using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.BlockDiff;
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
    private readonly ISimilarBlockFinder _blockFinder;
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
        ISimilarBlockFinder blockFinder,
        ThemeService themeService)
    {
        _settingsStore = settingsStore;
        _dialogService = dialogService;
        _fileSearchService = fileSearchService;
        _eventLogSearchService = eventLogSearchService;
        _blockFinder = blockFinder;
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
            // A .gz archive can't be tailed incrementally — decompress it once and open the plain copy,
            // keeping the original path for the recent-list and the tab title.
            string openPath;
            try
            {
                openPath = CompressedLogFile.Materialize(fullPath);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                openPath = fullPath;
            }

            var isCompressed = !string.Equals(openPath, fullPath, StringComparison.OrdinalIgnoreCase);
            var options = new TailSourceOptions { PollInterval = TimeSpan.FromMilliseconds(250) };
            var source = new FileTailSource(openPath, options);
            var detectedFormatId = LogLineParsers.DetectFile(openPath);
            var formatOverride = FindExistingFormatOverride(TailSourceKind.File, fullPath, pattern: null, eventLogChannel: null);
            var isStructuredView = FindExistingOverride(TailSourceKind.File, fullPath, pattern: null, eventLogChannel: null)
                ?? detectedFormatId is not null;
            document = AddDocument(source, openPath,
                title: isCompressed ? Path.GetFileName(fullPath) : null,
                isStructuredView: isStructuredView,
                structuredFormatId: formatOverride ?? detectedFormatId,
                structuredFormatManuallyChosen: formatOverride is not null);
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

    /// <summary>The persisted manual structured-format override (<c>serilog</c>/<c>ndjson</c>/…) for this
    /// source's dedup key, or null when the user has never pinned a format for it.</summary>
    private string? FindExistingFormatOverride(TailSourceKind kind, string path, string? pattern, string? eventLogChannel)
    {
        var dedupKey = ComputeDedupKey(kind, path, pattern, eventLogChannel);
        return _settings.RecentSources
            .FirstOrDefault(r => string.Equals(ComputeDedupKey(r), dedupKey, StringComparison.OrdinalIgnoreCase))
            ?.StructuredFormatId;
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

            // ActiveFilePath is only known after the ctor's Start(), but StructuredFormatId is fixed at
            // construction — so detect the format from the most-recently-modified match up front.
            var candidateFile = SafeLatestMatch(fullDirectory, pattern);
            var detectedFormatId = candidateFile is not null ? LogLineParsers.DetectFile(candidateFile) : null;
            var overrideValue = FindExistingOverride(TailSourceKind.DirectoryWatch, fullDirectory, pattern, eventLogChannel: null);
            var formatOverride = FindExistingFormatOverride(TailSourceKind.DirectoryWatch, fullDirectory, pattern, eventLogChannel: null);

            document = AddDocument(source, dedupKey, title,
                structuredFormatId: formatOverride ?? detectedFormatId,
                structuredFormatManuallyChosen: formatOverride is not null);
            document.IsStructuredView = overrideValue ?? detectedFormatId is not null;
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
    private void OpenRemoteEndpoint()
    {
        var selection = _dialogService.ShowOpenHttpTailDialog();
        if (selection is not null)
        {
            OpenRemoteEndpoint(selection.Url, selection.Mode, selection.Headers);
        }
    }

    /// <summary>Opens a remote log endpoint. A <c>ws://</c>/<c>wss://</c> URL is tailed over a WebSocket;
    /// an <c>http(s)://</c> URL is streamed or polled per <paramref name="mode"/>.</summary>
    public TailDocumentViewModel OpenRemoteEndpoint(string url, string mode, IReadOnlyList<string> headers)
    {
        var uri = new Uri(url);
        var isWebSocket = uri.Scheme is "ws" or "wss";
        var dedupKey = $"remote:{url}";

        if (!TryActivateExisting(dedupKey, out var document))
        {
            var headerMap = ParseHeaderLines(headers);
            ITailSource source = isWebSocket
                ? new WebSocketTailSource(uri, new WebSocketTailOptions { Headers = headerMap })
                : new HttpTailSource(uri, new HttpTailOptions
                {
                    Mode = Enum.TryParse<HttpTailMode>(mode, ignoreCase: true, out var m) ? m : HttpTailMode.Auto,
                    Headers = headerMap,
                });
            document = AddDocument(source, dedupKey, $"[{(isWebSocket ? "WS" : "HTTP")}] {uri.Host}");
        }

        RecordRecent(new TailSourceSettings
        {
            Kind = isWebSocket ? TailSourceKind.RemoteWebSocket : TailSourceKind.RemoteHttp,
            Path = url,
            HttpMode = mode,
            HttpHeaders = headers.ToList(),
        });
        return document!;
    }

    private static Dictionary<string, string> ParseHeaderLines(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var idx = line.IndexOf(':');
            if (idx > 0)
            {
                result[line[..idx].Trim()] = line[(idx + 1)..].Trim();
            }
        }

        return result;
    }

    [RelayCommand]
    private void OpenMergedFiles()
    {
        var paths = _dialogService.ShowOpenMergedSourcesDialog();
        if (paths is not { Count: >= 2 })
        {
            if (paths is { Count: 1 })
            {
                OpenPath(paths[0]);
            }

            return;
        }

        OpenMergedFiles(paths.Select(Path.GetFullPath).ToList());
    }

    public TailDocumentViewModel OpenMergedFiles(IReadOnlyList<string> paths)
    {
        var ordered = paths.Select(Path.GetFullPath).ToList();
        var dedupKey = MergedDedupKey(ordered);

        if (!TryActivateExisting(dedupKey, out var document))
        {
            var options = new TailSourceOptions { PollInterval = TimeSpan.FromMilliseconds(250) };
            var source = new MergedTailSource(ordered, options);

            // Detect a structured format from the underlying files (read raw, before the merge prefix).
            var detectedFormatId = ordered
                .Select(p => File.Exists(p) ? LogLineParsers.DetectFile(p) : null)
                .FirstOrDefault(f => f is not null);

            document = AddDocument(source, dedupKey, source.DisplayName,
                isStructuredView: detectedFormatId is not null,
                structuredFormatId: detectedFormatId);
        }

        RecordRecent(new TailSourceSettings { Kind = TailSourceKind.MergedFiles, Path = dedupKey, MergedPaths = ordered });
        return document!;
    }

    /// <summary>Order-independent dedup key for a merged-files source.</summary>
    private static string MergedDedupKey(IEnumerable<string> paths) =>
        "merged:" + string.Join('|', paths.Select(p => p.ToLowerInvariant()).OrderBy(p => p, StringComparer.Ordinal));

    [RelayCommand]
    private void OpenProcessTail()
    {
        var s = _dialogService.ShowOpenProcessTailDialog();
        if (s is not null)
        {
            OpenProcessTail(s.FileName, s.Arguments, s.RestartOnExit);
        }
    }

    public TailDocumentViewModel OpenProcessTail(string fileName, string arguments, bool restartOnExit)
    {
        var dedupKey = $"proc:{fileName} {arguments}".TrimEnd();

        if (!TryActivateExisting(dedupKey, out var document))
        {
            var source = new ProcessTailSource(new ProcessTailOptions
            {
                FileName = fileName,
                Arguments = arguments,
                RestartOnExit = restartOnExit,
            });
            document = AddDocument(source, dedupKey, source.DisplayName);
        }

        RecordRecent(new TailSourceSettings
        {
            Kind = TailSourceKind.Process,
            Path = dedupKey,
            ProcessFileName = fileName,
            ProcessArguments = arguments,
            ProcessRestartOnExit = restartOnExit,
        });
        return document!;
    }

    [RelayCommand]
    private void OpenSshTail()
    {
        var s = _dialogService.ShowOpenSshTailDialog();
        if (s is null)
        {
            return;
        }

        var options = new SshTailOptions
        {
            Host = s.Host,
            Port = s.Port,
            Username = s.Username,
            Password = s.Password,
            PrivateKeyPath = s.PrivateKeyPath,
            PrivateKeyPassphrase = s.PrivateKeyPassphrase,
            Command = s.Command,
            ExpectedHostKeyFingerprintSha256 = s.HostKeyFingerprintSha256,
            AcceptAnyHostKey = s.AcceptAnyHostKey,
        };
        OpenSshTail(options);

        RecordRecent(new TailSourceSettings
        {
            Kind = TailSourceKind.Ssh,
            Path = SshDedupKey(options),
            SshHost = s.Host,
            SshPort = s.Port,
            SshUsername = s.Username,
            SshPrivateKeyPath = s.PrivateKeyPath,
            SshHostKeyFingerprintSha256 = s.HostKeyFingerprintSha256,
            SshAcceptAnyHostKey = s.AcceptAnyHostKey,
            SshCommand = s.Command,
        });
    }

    public TailDocumentViewModel OpenSshTail(SshTailOptions options)
    {
        var dedupKey = SshDedupKey(options);
        if (!TryActivateExisting(dedupKey, out var document))
        {
            var source = new SshTailSource(options);
            document = AddDocument(source, dedupKey, source.DisplayName);
        }

        return document!;
    }

    private static string SshDedupKey(SshTailOptions o) => $"ssh:{o.Username}@{o.Host}:{o.Port}/{o.Command}";

    [RelayCommand]
    private void OpenEtwTail()
    {
        var s = _dialogService.ShowOpenEtwTailDialog();
        if (s is not null)
        {
            OpenEtwTail(s.Provider, s.Level);
        }
    }

    public TailDocumentViewModel OpenEtwTail(string provider, int level)
    {
        var dedupKey = $"etw:{provider}";
        if (!TryActivateExisting(dedupKey, out var document))
        {
            var source = new EtwTailSource(new EtwTailOptions { Provider = provider, Level = level });
            document = AddDocument(source, dedupKey, source.DisplayName);
        }

        RecordRecent(new TailSourceSettings
        {
            Kind = TailSourceKind.Etw,
            Path = dedupKey,
            EtwProvider = provider,
            EtwLevel = level,
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

    /// <summary>Most-recently-modified file in <paramref name="directory"/> matching <paramref name="pattern"/>,
    /// used only to sniff the structured format when opening a directory watch. Never throws.</summary>
    private static string? SafeLatestMatch(string directory, string pattern)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern).MaxBy(File.GetLastWriteTimeUtc)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private TailDocumentViewModel AddDocument(
        ITailSource source,
        string dedupKey,
        string? title = null,
        string? eventLogChannelName = null,
        IReadOnlyList<EventLogFilterRule>? eventLogFilters = null,
        bool isStructuredView = false,
        string? structuredFormatId = null,
        bool structuredFormatManuallyChosen = false)
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
            isStructuredView,
            structuredFormatId,
            structuredFormatManuallyChosen);
        document.SetInitialMdiBounds(Documents.Count);
        document.ApplyThemeMode(_currentThemeMode);
        document.ApplyColorizeStructuredValues(_settings.ColorizeStructuredValues);
        document.ApplyShowHighlightMatchSpans(_settings.HighlightMatchSpans);
        document.ApplyLogFontSize(_settings.LogFontSize);
        document.ApplyDetailPanelHeight(_settings.Layout.DetailPanelHeight);
        document.DetailPanelHeightChanged += height => OnDetailPanelHeightChanged(document, height);
        document.LogFontSizeChanged += fontSize => OnLogFontSizeChanged(document, fontSize);
        document.SearchRequested += () => ShowSearchDialog(document);
        document.CustomizeRequested += () => ShowCustomizeDialog(document);
        document.FindSimilarBlockRequested += line => ShowSimilarBlockDialog(document, line);

        Documents.Add(document);
        ActiveDocument = document;
        return document;
    }

    /// <summary>Persists a splitter-dragged detail-panel height and applies it to every other open
    /// document, so resizing one document's panel keeps every document in sync (equal-value assignments
    /// are no-ops via <c>ObservableProperty</c>'s equality check, so this can't loop).</summary>
    private void OnDetailPanelHeightChanged(TailDocumentViewModel source, double height)
    {
        _settings.Layout.DetailPanelHeight = height;
        foreach (var document in Documents)
        {
            if (document != source)
            {
                document.ApplyDetailPanelHeight(height);
            }
        }
    }

    /// <summary>Persists a Ctrl+MouseWheel-zoomed log font size and applies it to every other open document,
    /// so zooming one document's log view keeps every document in sync (equal-value assignments are no-ops
    /// via <c>ObservableProperty</c>'s equality check, so this can't loop).</summary>
    private void OnLogFontSizeChanged(TailDocumentViewModel source, double fontSize)
    {
        _settings.LogFontSize = fontSize;
        foreach (var document in Documents)
        {
            if (document != source)
            {
                document.ApplyLogFontSize(fontSize);
            }
        }
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

    private void ShowSimilarBlockDialog(TailDocumentViewModel document, LogLineViewModel anchorLine) =>
        _dialogService.ShowSimilarBlockDialog(document, anchorLine, Documents, _blockFinder);

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

        // Propagate the colorize-values and log-font-size settings to all currently open documents so the
        // change takes effect immediately without needing to reopen them.
        foreach (var document in Documents)
        {
            document.ApplyColorizeStructuredValues(_settings.ColorizeStructuredValues);
            document.ApplyShowHighlightMatchSpans(_settings.HighlightMatchSpans);
            document.ApplyLogFontSize(_settings.LogFontSize);
        }
    }

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current?.Shutdown();

    [RelayCommand]
    private void ShowCommandPalette()
    {
        var chosen = _dialogService.ShowCommandPalette(BuildPaletteCommands());
        chosen?.Execute();
    }

    /// <summary>Flattens the app's menu actions, every open document ("Go to…"), and the active
    /// document's own commands into one searchable list for the Ctrl+P palette.</summary>
    public IReadOnlyList<PaletteCommand> BuildPaletteCommands()
    {
        var list = new List<PaletteCommand>
        {
            new("Open File…", "File", () => OpenFileCommand.Execute(null)),
            new("Open Directory (Watch)…", "File", () => OpenDirectoryWatchCommand.Execute(null)),
            new("Open Merged Files / Folders…", "File", () => OpenMergedFilesCommand.Execute(null)),
            new("Open Windows Event Log…", "File", () => OpenEventLogCommand.Execute(null)),
            new("Open Remote Log Endpoint…", "File", () => OpenRemoteEndpointCommand.Execute(null)),
            new("Open Command Output…", "File", () => OpenProcessTailCommand.Execute(null)),
            new("Open SSH Log Tail…", "File", () => OpenSshTailCommand.Execute(null)),
            new("Open ETW Provider…", "File", () => OpenEtwTailCommand.Execute(null)),
            new("Window Mode: Tabbed", "Window", () => SwitchWindowModeCommand.Execute("Tabbed")),
            new("Window Mode: Floating", "Window", () => SwitchWindowModeCommand.Execute("Floating")),
            new("Window Mode: MDI", "Window", () => SwitchWindowModeCommand.Execute("Mdi")),
            new("Highlight Presets…", "Tools", () => EditHighlightPresetsCommand.Execute(null)),
            new("External Tools…", "Tools", () => EditExternalToolsCommand.Execute(null)),
            new("Windows Services…", "Tools", () => OpenServicesCommand.Execute(null)),
            new("Settings…", "Tools", () => OpenSettingsCommand.Execute(null)),
        };

        foreach (var toggle in HighlightPresetToggles)
        {
            var captured = toggle;
            list.Add(new PaletteCommand(
                $"Toggle Highlight Preset: {captured.Name}",
                "Tools",
                () => captured.IsEnabled = !captured.IsEnabled));
        }

        foreach (var document in Documents)
        {
            var captured = document;
            list.Add(new PaletteCommand($"Go to: {captured.DisplayTitle}", "Document", () => ActiveDocument = captured, captured.SourcePath));
        }

        if (ActiveDocument is { } active)
        {
            list.Add(new PaletteCommand("Toggle Follow Tail", "Active document", () => active.ToggleFollowCommand.Execute(null)));
            list.Add(new PaletteCommand("Toggle Structured View", "Active document", () => active.IsStructuredView = !active.IsStructuredView));
            list.Add(new PaletteCommand("Toggle Volume Timeline", "Active document", () => active.ToggleTimelineCommand.Execute(null)));
            list.Add(new PaletteCommand("Clear All Filters", "Active document", () => active.ClearFilterCommand.Execute(null)));
            list.Add(new PaletteCommand("Export Visible Lines…", "Active document", () => active.ExportVisibleCommand.Execute(null)));
            list.Add(new PaletteCommand("Search in Document…", "Active document", () => active.SearchCommand.Execute(null)));
            list.Add(new PaletteCommand("Customize Tab Color / Icon…", "Active document", () => active.CustomizeCommand.Execute(null)));
            list.Add(new PaletteCommand("Next Highlight", "Active document", () => active.NextHighlightCommand.Execute(null)));
            list.Add(new PaletteCommand("Previous Highlight", "Active document", () => active.PreviousHighlightCommand.Execute(null)));
            list.Add(new PaletteCommand("Toggle Bookmark", "Active document", () => active.ToggleBookmarkCommand.Execute(null)));
            list.Add(new PaletteCommand("Close Document", "Active document", () => CloseDocumentCommand.Execute(active)));
        }

        return list;
    }

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
            entry.StructuredFormatId = document.IsStructuredFormatManuallyChosen ? document.StructuredFormatId : null;
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
                TailSourceKind.MergedFiles when entry.MergedPaths.Count >= 2 && entry.MergedPaths.All(File.Exists) =>
                    OpenMergedFiles(entry.MergedPaths),
                TailSourceKind.RemoteHttp or TailSourceKind.RemoteWebSocket when !string.IsNullOrWhiteSpace(entry.Path) =>
                    OpenRemoteEndpoint(entry.Path, entry.HttpMode ?? "Auto", entry.HttpHeaders),
                TailSourceKind.Process when !string.IsNullOrWhiteSpace(entry.ProcessFileName) =>
                    OpenProcessTail(entry.ProcessFileName, entry.ProcessArguments ?? string.Empty, entry.ProcessRestartOnExit),
                TailSourceKind.Ssh when !string.IsNullOrWhiteSpace(entry.SshHost)
                    && !string.IsNullOrWhiteSpace(entry.SshUsername)
                    && !string.IsNullOrWhiteSpace(entry.SshCommand)
                    && !string.IsNullOrWhiteSpace(entry.SshPrivateKeyPath) =>
                    OpenSshTail(new SshTailOptions
                    {
                        Host = entry.SshHost,
                        Port = entry.SshPort,
                        Username = entry.SshUsername,
                        PrivateKeyPath = entry.SshPrivateKeyPath,
                        Command = entry.SshCommand,
                        ExpectedHostKeyFingerprintSha256 = entry.SshHostKeyFingerprintSha256,
                        AcceptAnyHostKey = entry.SshAcceptAnyHostKey,
                    }),
                TailSourceKind.Etw when !string.IsNullOrWhiteSpace(entry.EtwProvider) =>
                    OpenEtwTail(entry.EtwProvider, entry.EtwLevel),
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
        TailSourceKind.RemoteHttp or TailSourceKind.RemoteWebSocket => $"remote:{path}",
        // Process / Ssh / Etw store their already-composed dedup key in Path.
        _ => path,
    };
}
