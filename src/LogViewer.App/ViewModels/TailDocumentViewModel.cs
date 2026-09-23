using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Controls;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.Analysis;
using LogViewer.Core.Annotations;
using LogViewer.Core.Bookmarks;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

/// <summary>
/// The single document view-model shared by every window-hosting mode (Tabbed/Floating/MDI) — window
/// mode is purely a hosting strategy over this instance, never a reason to recreate it. Wraps one
/// <see cref="ITailSource"/>, its <see cref="RingLineBuffer"/>, a <see cref="HighlightEngine"/> and a
/// <see cref="BookmarkManager"/>.
/// </summary>
public sealed partial class TailDocumentViewModel : ObservableObject, IDisposable
{
    private readonly ITailSource _source;
    private readonly bool _isMergedSource;
    private readonly RingLineBuffer _buffer;
    private ILogLineParser _lineParser;
    private readonly HighlightEngine _highlightEngine = new();
    private readonly BookmarkManager _bookmarks = new();
    private readonly SortedSet<long> _highlightedLineNumbers = new();
    private readonly UiDispatcherLineSink _sink;
    private readonly Dictionary<Guid, DateTime> _lastAutoTriggerAt = new();
    private readonly SoundAlertSettings _soundAlertSettings;
    private readonly ISoundAlertPlayer _soundAlertPlayer;
    private readonly NotificationAlertSettings _notificationAlertSettings;
    private readonly INotificationService _notificationService;
    private readonly AlertWindowTracker _alertWindowTracker = new();

    /// <summary>Alerts this document raised (threshold hits, new error patterns), for the MCP <c>logs_get_alerts</c>
    /// tool — recorded even when desktop notifications are off.</summary>
    public AlertHistory Alerts { get; } = new();
    private readonly NewPatternDetector _newPatternDetector = new();
    private readonly SortedSet<long> _newPatternLineNumbers = new();
    private bool _initialLoadSeen;
    private DateTime _lastNewPatternNotifyAt = DateTime.MinValue;
    private static readonly TimeSpan NewPatternNotifyThrottle = TimeSpan.FromSeconds(10);
    private IReadOnlyDictionary<Guid, HighlightRule> _rulesById;
    private DateTime _lastSoundAlertAt = DateTime.MinValue;
    private static readonly TimeSpan SoundAlertThrottle = TimeSpan.FromSeconds(3);
    private static readonly int SoundAlertMinSeverityRank = LogLevelSeverity.Rank("Error")!.Value;
    private CancellationTokenSource? _reprocessCts;
    private bool _isReprocessing;
    private readonly List<LogLineViewModel> _pendingDuringReprocess = new();

    private IReadOnlyList<ExternalToolDefinition> _externalTools;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _customColorHex;

    [ObservableProperty]
    private string? _customIconGlyph;

    [ObservableProperty]
    private bool _isFollowingTail = true;

    /// <summary>Lines that have arrived since follow was paused (by scrolling up or the toggle). Drives
    /// the "N new lines — resume follow" banner; reset when follow resumes.</summary>
    [ObservableProperty]
    private int _unseenLineCount;

    /// <summary>True while the view is applying a programmatic scroll (ScrollIntoView) — the view's
    /// scroll-changed handler checks this so its own auto-scroll doesn't get mistaken for the user
    /// scrolling away and pause the follow it just performed.</summary>
    public bool IsProgrammaticScroll { get; set; }

    public string ResumeFollowBanner => UnseenLineCount > 0
        ? $"⤓ {UnseenLineCount:N0} new line{(UnseenLineCount == 1 ? string.Empty : "s")} — resume follow"
        : "⤓ New lines — resume follow";

    partial void OnUnseenLineCountChanged(int value) => OnPropertyChanged(nameof(ResumeFollowBanner));

    partial void OnIsFollowingTailChanged(bool value)
    {
        if (value)
        {
            UnseenLineCount = 0;
            HasUnseenChanges = false;
        }
    }

    /// <summary>Called by the view when the user scrolls up away from the tail — pauses follow so new
    /// lines don't yank the viewport back down.</summary>
    public void NotifyUserScrolledAwayFromEnd()
    {
        if (IsFollowingTail)
        {
            IsFollowingTail = false;
        }
    }

    /// <summary>Called by the view when the user scrolls back to the bottom — silently re-arms follow.</summary>
    public void NotifyUserScrolledToEnd()
    {
        if (!IsFollowingTail)
        {
            IsFollowingTail = true;
        }
    }

    [ObservableProperty]
    private bool _isStructuredView;

    [ObservableProperty]
    private bool _isColorizeStructuredValues = true;

    /// <summary>Per-window opt-in/out for the global sound alert on Error/Fatal lines. The sound itself
    /// (enabled, custom file) is a global setting — this toggle only decides whether this document plays it.</summary>
    [ObservableProperty]
    private bool _isSoundAlertEnabled = true;

    /// <summary>When true, the matched sub-string(s) of a highlight rule are emphasized within the line
    /// (bold + underline). Synced from the global setting via <see cref="ApplyShowHighlightMatchSpans"/>.</summary>
    [ObservableProperty]
    private bool _showHighlightMatchSpans = true;

    /// <summary>When true, auto-switching to a newer file in a directory-watch source inserts a marker
    /// line naming the newly active file. Synced from the global setting via <see cref="ApplyNotifyOnFileSwitch"/>.</summary>
    private bool _notifyOnFileSwitch = true;

    /// <summary>Syncs the global highlight-match-span setting to this document.</summary>
    public void ApplyShowHighlightMatchSpans(bool show) => ShowHighlightMatchSpans = show;

    private const double MinLogFontSize = 8;
    private const double MaxLogFontSize = 32;

    [ObservableProperty]
    private double _logFontSize = 12;

    /// <summary>Raised when the user Ctrl+MouseWheel-zooms this document's log font size, so <c>MainViewModel</c>
    /// can persist the new size and propagate it to every other open document.</summary>
    public event Action<double>? LogFontSizeChanged;

    partial void OnLogFontSizeChanged(double value) => LogFontSizeChanged?.Invoke(value);

    /// <summary>Applies a log font size from settings or another document's Ctrl+MouseWheel zoom, without
    /// re-raising <see cref="LogFontSizeChanged"/> for the same value.</summary>
    public void ApplyLogFontSize(double fontSize) => LogFontSize = fontSize;

    /// <summary>Nudges the log font size by <paramref name="steps"/> points (positive = larger), clamped to a
    /// readable range — driven by Ctrl+MouseWheel over the log view.</summary>
    public void AdjustLogFontSize(int steps) =>
        LogFontSize = Math.Clamp(LogFontSize + steps, MinLogFontSize, MaxLogFontSize);

    [ObservableProperty]
    private bool _hasUnseenChanges;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private LogLineViewModel? _selectedLine;

    /// <summary>Height (in pixels) of the structured-detail panel below the log lines, resized via a
    /// GridSplitter in <c>TailDocumentView</c> and shared across every open document via <see cref="ApplyDetailPanelHeight"/>.</summary>
    [ObservableProperty]
    private double _detailPanelHeight = 220;

    /// <summary>Raised when the user drags the detail-panel splitter, so <c>MainViewModel</c> can persist the
    /// new height and propagate it to every other open document.</summary>
    public event Action<double>? DetailPanelHeightChanged;

    partial void OnDetailPanelHeightChanged(double value) => DetailPanelHeightChanged?.Invoke(value);

    /// <summary>Applies a detail-panel height from settings or another document's splitter drag, without
    /// re-raising <see cref="DetailPanelHeightChanged"/> for the same value.</summary>
    public void ApplyDetailPanelHeight(double height) => DetailPanelHeight = height;

    [ObservableProperty]
    private string? _activeFilterField;

    [ObservableProperty]
    private string? _activeFilterValue;

    private const string AnyLevel = "Any";

    /// <summary>Options for the "Min Level" toolbar combo box — "Any" (no threshold) followed by every
    /// recognized severity, low to high, from <see cref="LogLevelSeverity"/>.</summary>
    public IReadOnlyList<string> LevelOptions { get; } = [AnyLevel, .. LogLevelSeverity.Levels];

    [ObservableProperty]
    private string _minLevel = AnyLevel;

    public bool IsLevelFilterActive => !string.Equals(MinLevel, AnyLevel, StringComparison.OrdinalIgnoreCase);

    /// <summary>The minimum severity rank to keep, or null when no level threshold is active — lines ranked
    /// at or above this pass the filter (e.g. selecting "Warning" keeps Warning, Error and Fatal).</summary>
    public int? MinLevelRank => IsLevelFilterActive ? LogLevelSeverity.Rank(MinLevel) : null;

    public bool IsFilterActive => ActiveFilterValue is not null || IsLevelFilterActive || IsTextFilterActive || IsHidingPastLines
        || IsTimeFilterActive || IsCorrelationFilterActive;

    // --- Visual "clean" — hides lines already displayed without touching the file or the ring buffer ---

    /// <summary>Lines with a line number below this are hidden from the view — purely a display-layer
    /// concern (like the trace/level/text filters), so nothing is evicted from <see cref="Lines"/> or the
    /// underlying file. Null means nothing is hidden.</summary>
    [ObservableProperty]
    private long? _hideBeforeLineNumber;

    public bool IsHidingPastLines => HideBeforeLineNumber is not null;

    partial void OnHideBeforeLineNumberChanged(long? value)
    {
        OnPropertyChanged(nameof(IsHidingPastLines));
        RaiseFilterChanged();
    }

    /// <summary>"Clean" the view: hides every line currently displayed, purely visually — new lines that
    /// arrive afterward (from the live tail) still show up normally. Does not modify the log file or evict
    /// anything from the ring buffer, so <see cref="ShowHiddenLines"/> brings everything straight back.</summary>
    [RelayCommand]
    private void ClearView()
    {
        if (Lines.Count > 0)
        {
            HideBeforeLineNumber = Lines[^1].LineNumber + 1;
        }
    }

    /// <summary>Hides every line above (i.e. before) the currently selected line — for cleaning up the view
    /// up to a specific point of interest rather than the whole visible history.</summary>
    [RelayCommand]
    private void HideLinesAboveSelected()
    {
        if (SelectedLine is not null)
        {
            HideBeforeLineNumber = SelectedLine.LineNumber;
        }
    }

    [RelayCommand]
    private void ShowHiddenLines() => HideBeforeLineNumber = null;

    // --- Live display filter over the raw line text (works in plain and structured view) -----------

    [ObservableProperty]
    private string? _textFilterPattern;

    /// <summary>When true, matching lines are <b>hidden</b> instead of being the only ones shown.</summary>
    [ObservableProperty]
    private bool _textFilterExclude;

    [ObservableProperty]
    private bool _textFilterIsRegex = true;

    [ObservableProperty]
    private bool _textFilterCaseSensitive;

    private System.Text.RegularExpressions.Regex? _compiledTextFilter;

    public bool IsTextFilterActive => !string.IsNullOrEmpty(TextFilterPattern);

    // --- Embedded pattern tester for the filter box (mirrors the one in the highlight editor) --------

    [ObservableProperty]
    private string _filterTesterInput = string.Empty;

    public IReadOnlyList<string> FilterTesterLines =>
        FilterTesterInput.Length == 0 ? [] : FilterTesterInput.Replace("\r\n", "\n").Split('\n');

    public string FilterTesterSummary
    {
        get
        {
            var lines = FilterTesterLines;
            if (lines.Count == 0 || string.IsNullOrEmpty(TextFilterPattern))
            {
                return string.Empty;
            }

            var hits = lines.Count(l => PatternMatchHelper.IsMatch(l, TextFilterPattern!, TextFilterIsRegex, TextFilterCaseSensitive));
            var verb = Loc.Get(TextFilterExclude ? "Vm_Doc_Tester_Hidden" : "Vm_Doc_Tester_Shown");
            var affected = TextFilterExclude ? lines.Count - hits : hits;
            return Loc.Format("Vm_Doc_Tester_Summary", hits, lines.Count, affected, verb);
        }
    }

    private void NotifyFilterTesterChanged()
    {
        OnPropertyChanged(nameof(FilterTesterLines));
        OnPropertyChanged(nameof(FilterTesterSummary));
    }

    partial void OnFilterTesterInputChanged(string value) => NotifyFilterTesterChanged();

    partial void OnTextFilterPatternChanged(string? value)
    {
        RebuildTextFilter();
        NotifyFilterTesterChanged();
    }

    partial void OnTextFilterExcludeChanged(bool value)
    {
        RaiseFilterChanged();
        NotifyFilterTesterChanged();
    }

    partial void OnTextFilterIsRegexChanged(bool value)
    {
        RebuildTextFilter();
        NotifyFilterTesterChanged();
    }

    partial void OnTextFilterCaseSensitiveChanged(bool value)
    {
        RebuildTextFilter();
        NotifyFilterTesterChanged();
    }

    private void RebuildTextFilter()
    {
        _compiledTextFilter = null;
        if (TextFilterIsRegex && !string.IsNullOrEmpty(TextFilterPattern))
        {
            try
            {
                var options = System.Text.RegularExpressions.RegexOptions.Compiled
                    | (TextFilterCaseSensitive ? System.Text.RegularExpressions.RegexOptions.None : System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                _compiledTextFilter = new System.Text.RegularExpressions.Regex(TextFilterPattern, options, TimeSpan.FromMilliseconds(250));
                StatusMessage = null;
            }
            catch (ArgumentException ex)
            {
                StatusMessage = Loc.Format("Vm_Doc_InvalidFilterRegex", ex.Message);
            }
        }

        RaiseFilterChanged();
    }

    /// <summary>Whether a line's raw text passes the live text filter — true when no filter is set.</summary>
    public bool PassesTextFilter(string lineText)
    {
        if (string.IsNullOrEmpty(TextFilterPattern))
        {
            return true;
        }

        bool matched;
        if (TextFilterIsRegex)
        {
            if (_compiledTextFilter is null)
            {
                return true; // invalid pattern — don't hide everything
            }

            try
            {
                matched = _compiledTextFilter.IsMatch(lineText);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return true;
            }
        }
        else
        {
            var comparison = TextFilterCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            matched = lineText.Contains(TextFilterPattern, comparison);
        }

        return TextFilterExclude ? !matched : matched;
    }

    [RelayCommand]
    private void ClearTextFilter() => TextFilterPattern = null;

    /// <summary>Raised by <see cref="ExportVisibleCommand"/>/<see cref="CopyVisibleCommand"/>/
    /// <see cref="CopyVisibleAsJsonCommand"/> — the view handles it because the effective (filtered, and
    /// possibly user-selected) line set lives in its <c>ICollectionView</c>/<c>ListView</c>, not in the
    /// view-model.</summary>
    public event Action<ExportTarget>? ExportRequested;

    [RelayCommand]
    private void ExportVisible() => ExportRequested?.Invoke(ExportTarget.File);

    [RelayCommand]
    private void CopyVisible() => ExportRequested?.Invoke(ExportTarget.Clipboard);

    [RelayCommand]
    private void CopyVisibleAsJson() => ExportRequested?.Invoke(ExportTarget.ClipboardJson);

    [RelayCommand]
    private void CopyVisibleFormatted() => ExportRequested?.Invoke(ExportTarget.ClipboardFormatted);

    public string? FilterStatusText
    {
        get
        {
            var parts = new List<string>(2);
            if (ActiveFilterValue is not null)
            {
                parts.Add($"{ActiveFilterField} = {ActiveFilterValue}");
            }

            if (IsLevelFilterActive)
            {
                parts.Add(Loc.Format("Vm_Doc_LevelAtLeast", MinLevel));
            }

            if (IsTextFilterActive)
            {
                parts.Add(Loc.Format("Vm_Doc_TextFilterPart", TextFilterExclude ? "≠" : "~", TextFilterPattern));
            }

            if (IsHidingPastLines)
            {
                parts.Add(Loc.Get("Vm_Doc_HiddenLinesPart"));
            }

            if (IsTimeFilterActive)
            {
                parts.Add(Loc.Format("Vm_Doc_TimeFilterPart", FormatFilterTime(TimeFilterFrom) ?? "…", FormatFilterTime(TimeFilterTo) ?? "…"));
            }

            if (CorrelationFilter is { } correlation)
            {
                parts.Add($"{correlation.Name} ~ {correlation.Value}");
            }

            return parts.Count > 0 ? Loc.Get("Vm_Doc_FilteredByPrefix") + string.Join(Loc.Get("Vm_Doc_FilterJoiner"), parts) : null;
        }
    }

    /// <summary>Raised whenever the active filter changes so the view can reapply its <c>ICollectionView</c>
    /// filter over <see cref="Lines"/> — filtering is a view-layer concern (WPF collection views), not
    /// something the view-model owns directly.</summary>
    public event Action? FilterChanged;

    partial void OnActiveFilterFieldChanged(string? value) => RaiseFilterChanged();

    partial void OnActiveFilterValueChanged(string? value) => RaiseFilterChanged();

    partial void OnMinLevelChanged(string value) => RaiseFilterChanged();

    private void RaiseFilterChanged()
    {
        OnPropertyChanged(nameof(IsFilterActive));
        OnPropertyChanged(nameof(FilterStatusText));
        OnPropertyChanged(nameof(IsLevelFilterActive));
        OnPropertyChanged(nameof(MinLevelRank));
        OnPropertyChanged(nameof(IsTextFilterActive));
        OnPropertyChanged(nameof(IsHidingPastLines));
        OnPropertyChanged(nameof(IsTimeFilterActive));
        OnPropertyChanged(nameof(IsCorrelationFilterActive));
        FilterChanged?.Invoke();
    }

    // MDI-mode child-window bounds (Phase 2). Only meaningful while the app is in MDI window mode;
    // Tabbed/Floating mode (AvalonDock) ignores these entirely.
    [ObservableProperty]
    private double _mdiLeft;

    [ObservableProperty]
    private double _mdiTop;

    [ObservableProperty]
    private double _mdiWidth = 480;

    [ObservableProperty]
    private double _mdiHeight = 320;

    [ObservableProperty]
    private bool _isMdiMaximized;

    [ObservableProperty]
    private int _mdiZIndex;

    private (double Left, double Top, double Width, double Height)? _mdiRestoreBounds;

    public TailDocumentViewModel(
        ITailSource source,
        string sourcePath,
        IReadOnlyList<HighlightPreset> highlightPresets,
        IReadOnlyList<ExternalToolDefinition> externalTools,
        SoundAlertSettings soundAlertSettings,
        ISoundAlertPlayer soundAlertPlayer,
        NotificationAlertSettings notificationAlertSettings,
        INotificationService notificationService,
        int ringBufferCapacity,
        TimeSpan uiRefreshInterval,
        string? title = null,
        string? eventLogChannelName = null,
        IReadOnlyList<EventLogFilterRule>? eventLogFilters = null,
        bool isStructuredView = false,
        string? structuredFormatId = null,
        bool structuredFormatManuallyChosen = false)
    {
        _source = source;
        _isMergedSource = source is MergedTailSource;
        SourcePath = sourcePath;
        _lineParser = LogLineParsers.Create(structuredFormatId) ?? new SerilogLogLineParser();
        _structuredFormatId = _lineParser.FormatId;
        IsStructuredFormatManuallyChosen = structuredFormatManuallyChosen;
        _buffer = new RingLineBuffer(ringBufferCapacity);
        Lines = new DisplayLineCollection(ringBufferCapacity);
        Lines.CollectionChanged += (_, _) =>
        {
            _structuredLinesCache = null;
            ScheduleTimelineRecompute();
        };
        SetHighlightRules(highlightPresets);
        _externalTools = externalTools;
        _soundAlertSettings = soundAlertSettings;
        _soundAlertPlayer = soundAlertPlayer;
        _notificationAlertSettings = notificationAlertSettings;
        _notificationService = notificationService;
        _title = title ?? (Path.GetFileName(sourcePath) is { Length: > 0 } fileName ? fileName : source.DisplayName);
        _isStructuredView = isStructuredView;
        _isColorizeStructuredValues = true;

        if (eventLogChannelName is not null)
        {
            SearchableEventLog = (eventLogChannelName, eventLogFilters ?? []);
        }

        _sink = new UiDispatcherLineSink(uiRefreshInterval);
        _sink.LinesFlushed += OnLinesFlushed;
        _sink.ResetFlushed += OnResetFlushed;

        _source.LinesRead += (_, e) => _sink.EnqueueLines(e.Lines);
        _source.SourceReset += (_, e) => _sink.EnqueueReset(e.Reason, e.SwitchedFilePath);
        _source.Error += (_, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusMessage = e.Exception.Message);

        LogLineParsers.CustomFormatsChanged += OnCustomFormatsChanged;

        _source.Start();
    }

    public string SourcePath { get; }

    /// <summary>The key that identifies this document in the recent-sources/session list. Usually the same as
    /// <see cref="SourcePath"/>; differs for a compressed file, whose <see cref="SourcePath"/> is the decompressed
    /// temp copy while the session remembers the original archive (and zip entry).</summary>
    public string SessionKey
    {
        get => _sessionKey ?? SourcePath;
        set
        {
            _sessionKey = value;
            ReloadNotes(); // notes are keyed by the session key
        }
    }

    private string? _sessionKey;

    private string _structuredFormatId;

    /// <summary>The <see cref="ILogLineParser.FormatId"/> used when <see cref="IsStructuredView"/> is on —
    /// auto-detected on open, or overridden by the user via the format picker. Setting it rebuilds the
    /// parser and reprocesses the buffer.</summary>
    public string StructuredFormatId
    {
        get => _structuredFormatId;
        set
        {
            if (string.Equals(_structuredFormatId, value, StringComparison.Ordinal) || LogLineParsers.Create(value) is not { } parser)
            {
                return;
            }

            _structuredFormatId = parser.FormatId;
            _lineParser = parser;
            IsStructuredFormatManuallyChosen = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StructuredFormatName));
            StructuredFormatChanged?.Invoke(_structuredFormatId);
            if (IsStructuredView)
            {
                _ = ReprocessAllLinesSafeAsync();
            }
        }
    }

    /// <summary>Raised when the user picks a different structured format, so <c>MainViewModel</c> can persist it.</summary>
    public event Action<string>? StructuredFormatChanged;

    /// <summary>True once the format was set explicitly (picker or restored override) rather than auto-detected —
    /// only then is <see cref="StructuredFormatId"/> persisted as a per-document override.</summary>
    public bool IsStructuredFormatManuallyChosen { get; private set; }


    /// <summary>Formats offered in the picker (user-defined custom formats first, then the built-ins), refreshed
    /// whenever the custom formats are edited.</summary>
    public IReadOnlyList<StructuredFormatOption> AvailableStructuredFormats { get; private set; } = StructuredFormatOption.All();

    /// <summary>Custom formats were added/edited/removed — refresh the picker, and rebuild this document's parser
    /// when it uses a custom format so an edited pattern takes effect without reopening the document.</summary>
    private void OnCustomFormatsChanged()
    {
        void Apply()
        {
            AvailableStructuredFormats = StructuredFormatOption.All();
            OnPropertyChanged(nameof(AvailableStructuredFormats));

            if (_structuredFormatId.StartsWith(CustomLogFormat.IdPrefix, StringComparison.Ordinal)
                && LogLineParsers.Create(_structuredFormatId) is { } parser)
            {
                _lineParser = parser;
                OnPropertyChanged(nameof(StructuredFormatName));
                if (IsStructuredView)
                {
                    _ = ReprocessAllLinesSafeAsync();
                }
            }
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke(Apply);
        }
    }

    /// <summary>Human-readable name of the active structured parser, shown next to the "Structured View" toggle.</summary>
    public string StructuredFormatName => _lineParser.DisplayName;

    public DisplayLineCollection Lines { get; }

    public IReadOnlyList<ExternalToolDefinition> ExternalTools => _externalTools;

    /// <summary>The real file path a full-file search should scan, or null when this document isn't file-backed
    /// (a directory watch reports whichever file it's currently tailing, which can change over time).</summary>
    public string? SearchableFilePath => _source switch
    {
        FileTailSource => SourcePath,
        DirectoryWatchTailSource dirWatch => dirWatch.ActiveFilePath,
        _ => null,
    };

    /// <summary>What kind of source this document wraps, mirroring <see cref="SearchableFilePath"/>'s switch —
    /// used e.g. by <see cref="Services.WpfOpenDocumentCatalog"/> to describe open documents to MCP tools.</summary>
    public TailSourceKind Kind => _source switch
    {
        FileTailSource => TailSourceKind.File,
        DirectoryWatchTailSource => TailSourceKind.DirectoryWatch,
        MergedTailSource => TailSourceKind.MergedFiles,
        HttpTailSource => TailSourceKind.RemoteHttp,
        WebSocketTailSource => TailSourceKind.RemoteWebSocket,
        ProcessTailSource => TailSourceKind.Process,
        SshTailSource => TailSourceKind.Ssh,
        EtwTailSource => TailSourceKind.Etw,
        _ => TailSourceKind.EventLog,
    };

    /// <summary>The EventLog channel + filters a full-channel search should scan, or null for file-backed documents.</summary>
    public (string Channel, IReadOnlyList<EventLogFilterRule> Filters)? SearchableEventLog { get; }

    public event Action? SearchRequested;

    public event Action? CustomizeRequested;

    public event Action? StatsRequested;

    /// <summary>Raised to open the SerilogTracing "Trace Tree" panel — null preselects nothing (toolbar
    /// button), a non-null value preselects that trace (context-menu "View Trace" on a specific line).</summary>
    public event Action<string?>? TraceTreeRequested;

    [RelayCommand]
    private void Search() => SearchRequested?.Invoke();

    [RelayCommand]
    private void Customize() => CustomizeRequested?.Invoke();

    [RelayCommand]
    private void ShowStats() => StatsRequested?.Invoke();

    [RelayCommand]
    private void ShowTraceTree() => TraceTreeRequested?.Invoke(null);

    [RelayCommand]
    private void ViewTrace(LogLineViewModel? line)
    {
        var traceId = StructuredFieldResolver.Resolve(line?.Structured, "TraceId");
        if (string.IsNullOrEmpty(traceId))
        {
            StatusMessage = Loc.Format("Vm_Doc_NoProperty", "TraceId");
            return;
        }

        TraceTreeRequested?.Invoke(traceId);
    }

    /// <summary>Applies an updated external-tool set for the "Run Tool" toolbar menu and auto-trigger matching.</summary>
    public void ApplyExternalTools(IReadOnlyList<ExternalToolDefinition> tools)
    {
        _externalTools = tools;
        OnPropertyChanged(nameof(ExternalTools));
    }

    /// <summary>Syncs the global colorize-structured-values setting to this document's own toggle.</summary>
    public void ApplyColorizeStructuredValues(bool colorize) => IsColorizeStructuredValues = colorize;

    /// <summary>Syncs the global "notify on directory-watch file switch" setting to this document.</summary>
    public void ApplyNotifyOnFileSwitch(bool notify) => _notifyOnFileSwitch = notify;

    [RelayCommand]
    private void RunExternalTool(ExternalToolDefinition? tool)
    {
        if (tool is null)
        {
            return;
        }

        var context = new ExternalToolContext(SearchableFilePath ?? SourcePath, SelectedLine?.LineNumber, SelectedLine?.Text);
        if (!ExternalToolLauncher.TryLaunch(tool, context, out var error))
        {
            StatusMessage = error;
        }
    }

    /// <summary>Fires any tool configured to auto-trigger on this highlight rule, throttled per-tool so a burst
    /// of matching lines doesn't spawn a process per line.</summary>
    private void TryAutoTriggerExternalTools(Guid ruleId, TailLine line)
    {
        var now = DateTime.UtcNow;
        foreach (var tool in _externalTools)
        {
            if (!tool.AutoTriggerOnHighlightMatch || tool.TriggerHighlightRuleId != ruleId)
            {
                continue;
            }

            if (_lastAutoTriggerAt.TryGetValue(tool.Id, out var last) && now - last < TimeSpan.FromSeconds(2))
            {
                continue;
            }

            _lastAutoTriggerAt[tool.Id] = now;
            var context = new ExternalToolContext(SearchableFilePath ?? SourcePath, line.LineNumber, line.Text);
            if (!ExternalToolLauncher.TryLaunch(tool, context, out var error))
            {
                StatusMessage = error;
            }
        }
    }

    /// <summary>Plays the global sound alert when <paramref name="line"/> is Error/Fatal severity, this
    /// document's toggle is on, and the global setting is enabled — throttled so a burst of matching
    /// lines plays at most one alert per <see cref="SoundAlertThrottle"/> window.</summary>
    private void TryPlaySoundAlert(TailLine line, StructuredLogEvent? structured)
    {
        if (!IsSoundAlertEnabled || !_soundAlertSettings.Enabled)
        {
            return;
        }

        if (DetectSeverityRank(line, structured) is not { } rank || rank < SoundAlertMinSeverityRank)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastSoundAlertAt < SoundAlertThrottle)
        {
            return;
        }

        _lastSoundAlertAt = now;
        _soundAlertPlayer.PlayAlert(_soundAlertSettings.CustomSoundFilePath);
    }

    /// <summary>Records an alert (and raises a desktop notification when notifications are on) once the matched
    /// rule has hit <c>AlertThresholdCount</c> times within its <c>AlertWindowSeconds</c> window (see
    /// <see cref="AlertWindowTracker"/>) — a burst of matching lines alerts exactly once, not once per line, since
    /// the tracker clears itself the moment the threshold is reached.</summary>
    private void TryRaiseThresholdAlert(Guid ruleId, TailLine line)
    {
        if (!_rulesById.TryGetValue(ruleId, out var rule) || !rule.AlertEnabled)
        {
            return;
        }

        var hit = _alertWindowTracker.RecordHit(
            ruleId, DateTime.UtcNow, rule.AlertThresholdCount, TimeSpan.FromSeconds(rule.AlertWindowSeconds));
        if (!hit)
        {
            return;
        }

        Alerts.Record(new AlertRecord(DateTimeOffset.Now, AlertKind.Threshold, rule.Name, line.LineNumber, line.Text));
        if (!_notificationAlertSettings.Enabled)
        {
            return;
        }

        _notificationService.Notify(
            Loc.Format("Vm_Alert_Title", Title),
            Loc.Format("Vm_Alert_Message", rule.Name, rule.AlertThresholdCount, rule.AlertWindowSeconds, line.LineNumber));
    }

    // --- New error patterns (anomaly detection) ----------------------------------------------------

    /// <summary>Warning/Error message shapes seen for the first time in this document's current content.</summary>
    [ObservableProperty]
    private int _newPatternCount;

    public string NewPatternBadge => NewPatternCount > 0 ? $"🆕 {NewPatternCount}" : "🆕";

    partial void OnNewPatternCountChanged(int value) => OnPropertyChanged(nameof(NewPatternBadge));

    /// <summary>Feeds one incoming line to the <see cref="NewPatternDetector"/>. Uses the structured level/message
    /// when the line parsed, else a level word and the raw text — no extra parse on the hot path. A newly seen
    /// shape is marked on the line, counted, and (after the initial file load, when the global opt-in is on)
    /// notified at most once per <see cref="NewPatternNotifyThrottle"/>.</summary>
    private void ObserveNewPattern(LogLineViewModel line)
    {
        var raw = TextForParsing(line.Text);
        var severity = LogLevelSeverity.Rank(line.Structured?.Level) ?? LogLevelNormalizer.GuessSeverityFromLine(raw);
        if (!_newPatternDetector.Observe(line.Structured?.RenderedMessage ?? raw, severity))
        {
            return;
        }

        line.IsNewPattern = true;
        _newPatternLineNumbers.Add(line.LineNumber);
        NewPatternCount++;

        if (_initialLoadSeen)
        {
            Alerts.Record(new AlertRecord(DateTimeOffset.Now, AlertKind.NewPattern, null, line.LineNumber, raw));
        }

        var now = DateTime.UtcNow;
        if (_initialLoadSeen
            && _notificationAlertSettings.Enabled
            && _notificationAlertSettings.NotifyOnNewErrorPatterns
            && now - _lastNewPatternNotifyAt >= NewPatternNotifyThrottle)
        {
            _lastNewPatternNotifyAt = now;
            var text = raw.Length > 160 ? raw[..160] + "…" : raw;
            _notificationService.Notify(Loc.Format("Vm_NewPattern_Title", Title), Loc.Format("Vm_NewPattern_Message", line.LineNumber, text));
        }
    }

    /// <summary>Same detection chain <see cref="RecomputeTimeline"/> uses: prefer the already-parsed
    /// structured level (when structured view produced one for this line), otherwise parse the raw text
    /// independently of the structured-view toggle, falling back to scanning for a level word.</summary>
    private int? DetectSeverityRank(TailLine line, StructuredLogEvent? structured)
    {
        if (structured?.Level is { } structuredLevel && LogLevelSeverity.Rank(structuredLevel) is { } structuredRank)
        {
            return structuredRank;
        }

        var raw = TextForParsing(line.Text);
        if (_lineParser.TryParse(raw, out var parsed) && parsed?.Level is { } parsedLevel)
        {
            return LogLevelSeverity.Rank(parsedLevel) ?? LogLevelNormalizer.GuessSeverityFromLine(raw);
        }

        return LogLevelNormalizer.GuessSeverityFromLine(raw);
    }

    /// <summary>Total lines ever appended (not bounded by the ring buffer), used for the title-bar lines/sec stat.</summary>
    public long TotalLinesAppended => _buffer.TotalLinesAppended;

    // --- Performance telemetry (polled ~1 Hz by MainViewModel for the status bar) ------------------
    public int BufferedLineCount => _buffer.Count;

    public int BufferCapacity => _buffer.Capacity;

    /// <summary>Approximate live text held in this document's ring buffer, in bytes (2 per char).</summary>
    public long BufferedTextBytes => _buffer.RetainedTextLength * 2;

    /// <summary>Smoothed UI-thread time per flush tick for this document's tail, in milliseconds.</summary>
    public double DispatchLatencyMs => _sink.AverageFlushMilliseconds;

    /// <summary>Title prefixed with the custom glyph (if set) and a change marker while unseen changes are
    /// pending — drives the tab/MDI-title-bar text.</summary>
    public string DisplayTitle
    {
        get
        {
            var glyph = string.IsNullOrEmpty(CustomIconGlyph) ? string.Empty : $"{CustomIconGlyph} ";
            var marker = HasUnseenChanges ? "● " : string.Empty;
            return $"{glyph}{marker}{Title}";
        }
    }

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnHasUnseenChangesChanged(bool value) => OnPropertyChanged(nameof(DisplayTitle));

    partial void OnCustomIconGlyphChanged(string? value) => OnPropertyChanged(nameof(DisplayTitle));

    /// <summary>Applies updated highlight presets, live-recoloring every currently displayed line to match.</summary>
    public void ApplyHighlightPresets(IReadOnlyList<HighlightPreset> presets)
    {
        SetHighlightRules(presets);
        ReapplyHighlighting();
    }

    /// <summary>Flattens <paramref name="presets"/> into the matching engine and keeps the rule-lookup table
    /// in sync, so <see cref="TryRaiseThresholdAlert"/> can look up a matched rule's alert configuration
    /// (not carried by <see cref="HighlightMatch"/> itself).</summary>
    private void SetHighlightRules(IReadOnlyList<HighlightPreset> presets)
    {
        var flattened = HighlightPreset.FlattenForMatching(presets).ToList();
        _highlightEngine.SetRules(flattened);
        _rulesById = flattened.ToDictionary(r => r.Id);
    }

    /// <summary>Switches which color pair highlight matches resolve to (see <see cref="HighlightRule.ResolveColors"/>),
    /// live-recoloring every currently displayed line to match.</summary>
    public void ApplyThemeMode(ThemeBaseMode mode)
    {
        _highlightEngine.SetThemeMode(mode);
        ReapplyHighlighting();
    }

    /// <summary>Re-evaluates every line currently in <see cref="Lines"/> against the current rules/theme —
    /// each line's own <see cref="LogLineViewModel.Foreground"/>/<see cref="LogLineViewModel.Background"/>
    /// are <c>ObservableProperty</c>s, so this repaints in place without a collection reset.</summary>
    private void ReapplyHighlighting()
    {
        ScrollMarkersInvalidated?.Invoke();
        _highlightedLineNumbers.Clear();
        foreach (var line in Lines)
        {
            var match = _highlightEngine.Evaluate(line.Text, line.Structured);
            line.ApplyMatch(match);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
            }
        }
    }

    /// <summary>The text a structured parser should see for a displayed line — with the <c>label│ </c>
    /// prefix removed for a merged-files document so its lines still parse as JSON/logfmt/etc.</summary>
    private string TextForParsing(string displayText) =>
        _isMergedSource ? MergedTailSource.StripLabel(displayText) : displayText;

    partial void OnIsStructuredViewChanged(bool value) => _ = ReprocessAllLinesSafeAsync();

    /// <summary>Fire-and-forget wrapper around <see cref="ReprocessAllLinesAsync"/> — the property changed
    /// handler can't be async itself, so without this an exception (e.g. a future parser change that throws
    /// instead of returning false) would become an unobserved task exception and vanish silently, leaving the
    /// document stuck mid-reprocess with <see cref="_isReprocessing"/> never cleared.</summary>
    private async Task ReprocessAllLinesSafeAsync()
    {
        try
        {
            await ReprocessAllLinesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusMessage = Loc.Format("Vm_Doc_SwitchStructuredFailed", ex.Message);
            _isReprocessing = false;
        }
    }

    /// <summary>Rebuilds every displayed line from its raw text when <see cref="IsStructuredView"/> is toggled —
    /// unlike highlight colors, <see cref="LogLineViewModel.Structured"/> isn't a mutable per-line property, so
    /// the display items themselves need replacing rather than just re-evaluated in place. Stays on the UI
    /// thread throughout (JSON parsing, highlight matching and <see cref="LogLineViewModel"/>'s brush cache are
    /// all plain, non-thread-safe state shared with the live tail path), but periodically yields via
    /// <see cref="Dispatcher.Yield(DispatcherPriority)"/> so a large buffer doesn't freeze the UI for one long
    /// synchronous pass — input and rendering get interleaved between chunks instead. While reprocessing is in
    /// flight, live-tailed lines that arrive from <see cref="OnLinesFlushed"/> are diverted into
    /// <see cref="_pendingDuringReprocess"/> instead of the ring-buffered <see cref="Lines"/> snapshot this
    /// method took at the start — without that, the closing <c>Lines.Clear()</c>/<c>AppendRange(rebuilt)</c>
    /// would silently wipe out anything tailed in during the yields (visible as "lines go missing" when
    /// toggling Structured View while a file is actively being written to).</summary>
    private async Task ReprocessAllLinesAsync()
    {
        const int ChunkSize = 500;

        _reprocessCts?.Cancel();
        var cts = new CancellationTokenSource();
        _reprocessCts = cts;
        var token = cts.Token;

        _isReprocessing = true;
        _pendingDuringReprocess.Clear();

        try
        {
            var isStructuredView = IsStructuredView;
            var selectedLineNumber = SelectedLine?.LineNumber;
            _continuationOwner = null;
            var snapshot = Lines.ToList();
            var rebuilt = new List<LogLineViewModel>(snapshot.Count);
            var highlighted = new SortedSet<long>();

            for (var i = 0; i < snapshot.Count; i++)
            {
                var existing = snapshot[i];
                var structured = isStructuredView && _lineParser.TryParse(TextForParsing(existing.Text), out var parsed) ? parsed : null;
                var match = _highlightEngine.Evaluate(existing.Text, structured);
                if (match is not null)
                {
                    highlighted.Add(existing.LineNumber);
                }

                var rebuiltLine = CreateLine(existing.LineNumber, existing.Text, structured, match, existing.IsBookmarked);
                rebuiltLine.IsNewPattern = existing.IsNewPattern;
                rebuilt.Add(rebuiltLine);

                if (i % ChunkSize == ChunkSize - 1)
                {
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            // Stale highlight entries for the reprocessed range only — anything a concurrent
            // OnLinesFlushed already added for pending lines (line numbers beyond the snapshot) must survive.
            var maxSnapshotLineNumber = snapshot.Count > 0 ? snapshot[^1].LineNumber : long.MinValue;
            _highlightedLineNumbers.RemoveWhere(n => n <= maxSnapshotLineNumber);
            foreach (var lineNumber in highlighted)
            {
                _highlightedLineNumbers.Add(lineNumber);
            }

            if (NeedsTimestamps)
            {
                ResolveTimestamps(rebuilt, recomputeDeltas: true);
            }

            Lines.Clear();
            Lines.AppendRange(rebuilt);
            if (_pendingDuringReprocess.Count > 0)
            {
                if (NeedsTimestamps)
                {
                    ResolveNewLines(_pendingDuringReprocess);
                }

                Lines.AppendRange(_pendingDuringReprocess);
                _pendingDuringReprocess.Clear();
            }

            TrimEvictedLineNumbers();

            SelectedLine = selectedLineNumber is { } selected ? Lines.FindByLineNumber(selected) : null;

            if (IsFollowingTail)
            {
                ScrollToEndRequested?.Invoke();
            }
        }
        finally
        {
            _isReprocessing = false;
        }
    }

    /// <summary>Staggers this document's initial MDI position so newly opened documents don't fully overlap.</summary>
    public void SetInitialMdiBounds(int openOrderIndex)
    {
        const double offset = 28;
        MdiLeft = 12 + (openOrderIndex % 8) * offset;
        MdiTop = 12 + (openOrderIndex % 8) * offset;
    }

    /// <summary>Toggles between the document's restored bounds and filling the given MDI viewport size.</summary>
    public void ToggleMdiMaximize(double viewportWidth, double viewportHeight)
    {
        if (IsMdiMaximized)
        {
            if (_mdiRestoreBounds is { } restore)
            {
                MdiLeft = restore.Left;
                MdiTop = restore.Top;
                MdiWidth = restore.Width;
                MdiHeight = restore.Height;
            }

            IsMdiMaximized = false;
        }
        else
        {
            _mdiRestoreBounds = (MdiLeft, MdiTop, MdiWidth, MdiHeight);
            MdiLeft = 0;
            MdiTop = 0;
            MdiWidth = Math.Max(viewportWidth, MdiWidth);
            MdiHeight = Math.Max(viewportHeight, MdiHeight);
            IsMdiMaximized = true;
        }
    }

    // --- Multi-line entries (custom formats with "join continuation lines") ------------------------

    /// <summary>The last structured entry seen, in ingestion order — owner of any following unmatched lines.</summary>
    private (long LineNumber, string? Level)? _continuationOwner;

    private bool JoinsContinuationLines => IsStructuredView && _lineParser is RegexLogLineParser { JoinsContinuationLines: true };

    /// <summary>Builds a display line; with a joining custom format, an unmatched line after an entry is tagged as
    /// that entry's continuation and inherits its level. Must be called in line order (it tracks the owner).</summary>
    private LogLineViewModel CreateLine(long lineNumber, string text, StructuredLogEvent? structured, HighlightMatch? match, bool isBookmarked)
    {
        LogLineViewModel line;
        if (structured is not null)
        {
            _continuationOwner = (lineNumber, structured.Level);
            line = new LogLineViewModel(lineNumber, text, structured, match, isBookmarked);
        }
        else if (JoinsContinuationLines && _continuationOwner is { } owner && text.Length > 0)
        {
            line = new LogLineViewModel(lineNumber, text, structured, match, isBookmarked)
            {
                ContinuationOf = owner.LineNumber,
                InheritedLevel = owner.Level,
            };
        }
        else
        {
            line = new LogLineViewModel(lineNumber, text, structured, match, isBookmarked);
        }

        ApplyNote(line);
        return line;
    }

    // --- Line notes (annotations) -------------------------------------------------------------------

    private ILineAnnotationStore? _annotationStore;

    /// <summary>This document's notes by line number; empty until a store is attached, for non-file documents, and
    /// for files nobody annotated (so <see cref="ApplyNote"/> costs a single Count check on the hot path).</summary>
    private Dictionary<long, LineAnnotation> _notes = [];

    /// <summary>Notes need a stable line numbering of one file: plain or compressed files, not merged views, directory
    /// watches (whose file changes), commands or event logs.</summary>
    public bool CanAnnotate => _annotationStore is not null && Kind == TailSourceKind.File && !_isMergedSource;

    /// <summary>Every note stored for this file (including ones whose line has since changed), for the MCP tools.</summary>
    public IReadOnlyList<LineAnnotation> Notes => [.. _notes.Values.OrderBy(n => n.LineNumber)];

    /// <summary>Raised by <see cref="EditNoteCommand"/>; the shell prompts for the text and calls <see cref="SetNote"/>.</summary>
    public event Action<LogLineViewModel>? EditNoteRequested;

    public void AttachAnnotationStore(ILineAnnotationStore store)
    {
        _annotationStore = store;
        ReloadNotes();
    }

    private void ReloadNotes()
    {
        _notes = CanAnnotate
            ? _annotationStore!.Get(SessionKey).ToDictionary(n => n.LineNumber)
            : [];
        foreach (var line in Lines)
        {
            line.Note = null;
            ApplyNote(line);
        }

        ScrollMarkersInvalidated?.Invoke();
    }

    /// <summary>Shows a stored note on its line only while the line still has the text the note was written on.</summary>
    private void ApplyNote(LogLineViewModel line)
    {
        if (_notes.Count > 0
            && _notes.TryGetValue(line.LineNumber, out var note)
            && note.TextHash == LineAnnotationStore.HashText(line.Text))
        {
            line.Note = note.Note;
        }
    }

    [RelayCommand]
    private void EditNote(LogLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line is not null && line.LineNumber > 0 && CanAnnotate)
        {
            EditNoteRequested?.Invoke(line);
        }
    }

    [RelayCommand]
    private void RemoveNote(LogLineViewModel? line)
    {
        line ??= SelectedLine;
        if (line?.HasNote == true)
        {
            SetNote(line, null);
        }
    }

    /// <summary>Adds, replaces or (for empty text) removes the note on <paramref name="line"/> and persists them.</summary>
    public void SetNote(LogLineViewModel line, string? text)
    {
        if (!CanAnnotate)
        {
            return;
        }

        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            _notes.Remove(line.LineNumber);
            line.Note = null;
        }
        else
        {
            _notes[line.LineNumber] = new LineAnnotation(line.LineNumber, LineAnnotationStore.HashText(line.Text), trimmed, DateTimeOffset.Now);
            line.Note = trimmed;
        }

        _annotationStore!.Set(SessionKey, [.. _notes.Values]);
        ScrollMarkersInvalidated?.Invoke();
    }

    /// <summary>The continuation lines (stack frames, wrapped text) of the selected entry, for the detail panel.</summary>
    [ObservableProperty]
    private string? _selectedEntryContinuation;

    partial void OnSelectedLineChanged(LogLineViewModel? value)
    {
        if (value?.Structured is null)
        {
            SelectedEntryContinuation = null;
            return;
        }

        var continuation = new List<string>();
        var index = Lines.IndexOf(value);
        for (var i = index + 1; index >= 0 && i < Lines.Count && continuation.Count < 500; i++)
        {
            if (Lines[i].ContinuationOf != value.LineNumber)
            {
                break;
            }

            continuation.Add(Lines[i].Text);
        }

        SelectedEntryContinuation = continuation.Count > 0 ? string.Join(Environment.NewLine, continuation) : null;
    }

    // --- Search hits shown on the scroll-marker strip ---------------------------------------------

    private readonly HashSet<long> _searchHitLineNumbers = [];

    /// <summary>True when <paramref name="lineNumber"/> is a result of this document's open full-text search.</summary>
    public bool IsSearchHit(long lineNumber) => _searchHitLineNumbers.Contains(lineNumber);

    public int SearchHitCount => _searchHitLineNumbers.Count;

    /// <summary>Called by the Search window as results stream in (and with an empty list when a new search starts
    /// or the window closes), so the overview strip marks where the matches are.</summary>
    public void SetSearchHits(IEnumerable<long> lineNumbers)
    {
        _searchHitLineNumbers.Clear();
        AddSearchHits(lineNumbers);
    }

    public void AddSearchHits(IEnumerable<long> lineNumbers)
    {
        _searchHitLineNumbers.UnionWith(lineNumbers);
        ScrollMarkersInvalidated?.Invoke();
    }

    /// <summary>Raised when something the scroll-marker strip draws changed without a collection change on
    /// <see cref="Lines"/> (bookmark toggled, highlight rules/theme re-applied), so the view refreshes the strip.</summary>
    public event Action? ScrollMarkersInvalidated;

    public event Action? ScrollToEndRequested;

    public event Action? ScrollToStartRequested;

    public event Action<LogLineViewModel>? ScrollToLineRequested;

    /// <summary>Jumps to the first line currently retained in the ring buffer (not necessarily line 1 of
    /// the file — older lines may already have scrolled out of the buffer). Pauses follow, same as
    /// scrolling up manually, so the newly-visible top of the buffer doesn't immediately get yanked away
    /// by the next tailed line.</summary>
    [RelayCommand]
    private void GoToStart()
    {
        IsFollowingTail = false;
        ScrollToStartRequested?.Invoke();
    }

    /// <summary>Jumps to the newest line and resumes follow — same effect as <see cref="ResumeFollow"/>,
    /// exposed as its own command so a permanent toolbar button works regardless of whether the
    /// "resume follow" banner is currently shown.</summary>
    [RelayCommand]
    private void GoToEnd() => ResumeFollow();

    private void OnLinesFlushed(IReadOnlyList<TailLine> lines)
    {
        var displayItems = new List<LogLineViewModel>(lines.Count);
        foreach (var line in lines)
        {
            var structured = IsStructuredView && _lineParser.TryParse(TextForParsing(line.Text), out var parsed) ? parsed : null;
            var match = _highlightEngine.Evaluate(line.Text, structured);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
                TryAutoTriggerExternalTools(match.RuleId, line);
                TryRaiseThresholdAlert(match.RuleId, line);
            }

            TryPlaySoundAlert(line, structured);

            var item = CreateLine(line.LineNumber, line.Text, structured, match, _bookmarks.IsBookmarked(line.LineNumber));
            ObserveNewPattern(item);
            displayItems.Add(item);
        }

        _initialLoadSeen = true;

        _buffer.AppendRange(lines);

        if (_isReprocessing)
        {
            // ReprocessAllLinesAsync took a snapshot of Lines and will replace it wholesale when done —
            // append there directly and it either gets clobbered or duplicated. Queue instead; the
            // reprocess appends this queue onto its rebuilt list once it finishes.
            _pendingDuringReprocess.AddRange(displayItems);
            return;
        }

        if (NeedsTimestamps)
        {
            // Before AppendRange: its Reset re-runs the view's time filter synchronously over these lines.
            ResolveNewLines(displayItems);
        }

        Lines.AppendRange(displayItems);
        TrimEvictedLineNumbers();

        if (_applyTimeFilterOnFirstLines && Lines.Count > 0)
        {
            // A restored/applied time filter with relative or time-of-day bounds needs lines to resolve against.
            _applyTimeFilterOnFirstLines = false;
            ApplyTimeFilter();
        }

        if (IsFollowingTail)
        {
            ScrollToEndRequested?.Invoke();
        }
        else
        {
            HasUnseenChanges = true;
            UnseenLineCount += displayItems.Count;
        }
    }

    private void OnResetFlushed(TailResetReason reason, string? switchedFilePath)
    {
        // A source reset (truncate/rotate) invalidates whatever snapshot a concurrent
        // ReprocessAllLinesAsync took — cancel it so it doesn't later overwrite this reset with stale lines.
        _reprocessCts?.Cancel();
        _isReprocessing = false;
        _pendingDuringReprocess.Clear();

        _buffer.Clear();
        Lines.Clear();
        _bookmarks.Clear();
        _highlightedLineNumbers.Clear();
        _newPatternDetector.Reset();
        _newPatternLineNumbers.Clear();
        NewPatternCount = 0;
        _continuationOwner = null;

        var markerText = switchedFilePath is not null && _notifyOnFileSwitch
            ? $"── {Loc.Format("Vm_Doc_SwitchedToFile", Path.GetFileName(switchedFilePath))} ──"
            : $"── file {reason.ToString().ToLowerInvariant()} — resuming ──";
        var marker = new LogLineViewModel(0, markerText, structured: null, match: null, isBookmarked: false);
        Lines.AppendRange([marker]);
        StatusMessage = switchedFilePath is not null
            ? Loc.Format("Vm_Doc_SwitchedToFile", Path.GetFileName(switchedFilePath))
            : Loc.Format("Vm_Doc_SourceResumed", reason.ToString().ToLowerInvariant());
    }

    private void TrimEvictedLineNumbers()
    {
        if (Lines.Count == 0)
        {
            return;
        }

        var oldestRetained = Lines[0].LineNumber;
        while (_highlightedLineNumbers.Count > 0 && _highlightedLineNumbers.Min < oldestRetained)
        {
            _highlightedLineNumbers.Remove(_highlightedLineNumbers.Min);
        }

        while (_newPatternLineNumbers.Count > 0 && _newPatternLineNumbers.Min < oldestRetained)
        {
            _newPatternLineNumbers.Remove(_newPatternLineNumbers.Min);
        }
    }

    [RelayCommand]
    private void ToggleFollow() => IsFollowingTail = !IsFollowingTail;

    [RelayCommand]
    private void ResumeFollow()
    {
        IsFollowingTail = true;
        HasUnseenChanges = false;
        ScrollToEndRequested?.Invoke();
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        if (SelectedLine is null)
        {
            return;
        }

        _bookmarks.Toggle(SelectedLine.LineNumber);
        SelectedLine.IsBookmarked = _bookmarks.IsBookmarked(SelectedLine.LineNumber);
        ScrollMarkersInvalidated?.Invoke();
    }

    /// <summary>Toggles the bookmark on an arbitrary line number — used by the Search dialog, which
    /// operates on <see cref="LogViewer.Core.Search.SearchResult"/> line numbers rather than the
    /// currently selected <see cref="LogLineViewModel"/>. Syncs the displayed row's glyph when that line
    /// is still present in the ring buffer; the bookmark itself is tracked by line number regardless.</summary>
    public bool ToggleBookmarkAt(long lineNumber)
    {
        _bookmarks.Toggle(lineNumber);
        var isBookmarked = _bookmarks.IsBookmarked(lineNumber);
        var line = Lines.FindByLineNumber(lineNumber);
        if (line is not null)
        {
            line.IsBookmarked = isBookmarked;
        }

        ScrollMarkersInvalidated?.Invoke();
        return isBookmarked;
    }

    /// <summary>The user's bookmarks as ascending absolute line numbers (a snapshot — for the MCP catalog).</summary>
    public IReadOnlyList<long> BookmarkedLineNumbers => [.. _bookmarks.Bookmarks.Select(b => b.LineNumber).Order()];

    /// <summary>Bookmarks an arbitrary line number if it isn't already — used by the Search dialog's
    /// "bookmark all results" action, which should never accidentally remove an existing bookmark.</summary>
    public void EnsureBookmarked(long lineNumber)
    {
        if (_bookmarks.IsBookmarked(lineNumber))
        {
            return;
        }

        ToggleBookmarkAt(lineNumber);
    }

    [RelayCommand]
    private void FilterByTraceId(LogLineViewModel? line) => ApplyPropertyFilter(line, "TraceId");

    [RelayCommand]
    private void FilterBySpanId(LogLineViewModel? line) => ApplyPropertyFilter(line, "SpanId");

    [RelayCommand]
    private void FilterByThreadId(LogLineViewModel? line) => ApplyPropertyFilter(line, "ThreadId");

    /// <summary>Raised when the user asks to find a similar block of logs elsewhere, anchored at
    /// <c>line</c> — <see cref="MainViewModel"/> opens the comparison dialog in response.</summary>
    public event Action<LogLineViewModel>? FindSimilarBlockRequested;

    [RelayCommand]
    private void FindSimilarBlock(LogLineViewModel? line)
    {
        if (line?.Structured is null)
        {
            StatusMessage = Loc.Get("Vm_Doc_NotStructured");
            return;
        }

        FindSimilarBlockRequested?.Invoke(line);
    }

    private List<(long LineNumber, StructuredLogEvent Event)>? _structuredLinesCache;

    /// <summary>Every currently displayed line that parsed as a structured event, in display order — the
    /// pool <see cref="Core.BlockDiff.LogBlockExtractor"/> extracts the anchor block from. Cached until
    /// <see cref="Lines"/> next raises its Reset notification (<see cref="DisplayLineCollection.AppendRange"/>/
    /// <see cref="DisplayLineCollection.Clear"/>), since this is re-read on every similar-block lookup but the
    /// underlying lines only change on tail flush, reset, or a structured-view toggle.</summary>
    public IReadOnlyList<(long LineNumber, StructuredLogEvent Event)> StructuredLines =>
        _structuredLinesCache ??= Lines.Where(l => l.Structured is not null).Select(l => (l.LineNumber, l.Structured!)).ToList();

    // --- Volume timeline -------------------------------------------------------------------------

    /// <summary>Fixed-width time buckets of the currently displayed lines' volume, for the timeline strip.
    /// Only lines that carry a parsed timestamp (structured view) contribute.</summary>
    public System.Collections.ObjectModel.ObservableCollection<VolumeBin> VolumeBins { get; } = [];

    [ObservableProperty]
    private bool _showTimeline;

    /// <summary>Largest <see cref="VolumeBin.Total"/> in <see cref="VolumeBins"/>, for bar-height normalization in the view.</summary>
    [ObservableProperty]
    private int _maxBinTotal = 1;

    /// <summary>True once at least two timestamped lines exist, so the timeline has something to show.</summary>
    [ObservableProperty]
    private bool _timelineHasData;

    private DispatcherTimer? _timelineRecomputeTimer;

    partial void OnShowTimelineChanged(bool value)
    {
        if (value)
        {
            RecomputeTimeline();
        }
        else
        {
            VolumeBins.Clear();
        }
    }

    private void ScheduleTimelineRecompute()
    {
        if (!ShowTimeline)
        {
            return;
        }

        _timelineRecomputeTimer ??= CreateTimelineTimer();
        _timelineRecomputeTimer.Stop();
        _timelineRecomputeTimer.Start();
    }

    private DispatcherTimer CreateTimelineTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RecomputeTimeline();
        };
        return timer;
    }

    private void RecomputeTimeline()
    {
        var samples = new List<VolumeSample>(Lines.Count);
        foreach (var line in Lines)
        {
            DateTimeOffset? timestamp;
            int severity;

            var raw = TextForParsing(line.Text);

            if (line.Structured?.Timestamp is { } structuredTs)
            {
                timestamp = structuredTs;
                severity = LogLevelSeverity.Rank(line.Structured.Level) ?? 2;
            }
            else if (_lineParser.TryParse(raw, out var parsed) && parsed?.Timestamp is { } parsedTs)
            {
                // Structured view is off but the line still parses (ndjson/logfmt/serilog/etc.).
                timestamp = parsedTs;
                severity = LogLevelSeverity.Rank(parsed.Level) ?? LogLevelNormalizer.GuessSeverityFromLine(raw) ?? 2;
            }
            else
            {
                // Plain-text line: pull a leading timestamp and a level word out of the raw text.
                timestamp = MergedTimestampExtractor.TryExtract(raw);
                severity = LogLevelNormalizer.GuessSeverityFromLine(raw) ?? 2;
            }

            if (timestamp is { } ts)
            {
                samples.Add(new VolumeSample(ts, severity, line.LineNumber));
            }
        }

        TimelineHasData = samples.Count >= 2;
        var bins = VolumeSpikeDetector.Detect(LogVolumeBinner.Bin(samples));

        VolumeBins.Clear();
        var max = 1;
        foreach (var bin in bins)
        {
            VolumeBins.Add(bin);
            if (bin.Total > max)
            {
                max = bin.Total;
            }
        }

        MaxBinTotal = max;
    }

    [RelayCommand]
    private void ToggleTimeline() => ShowTimeline = !ShowTimeline;

    [RelayCommand]
    private void SelectBin(VolumeBin? bin)
    {
        if (bin is null || bin.FirstLineNumber < 0)
        {
            return;
        }

        var target = Lines.FindByLineNumber(bin.FirstLineNumber);
        if (target is not null)
        {
            SelectedLine = target;
            IsFollowingTail = false;
            ScrollToLineRequested?.Invoke(target);
        }
    }

    [RelayCommand]
    private void FilterByProperty(object? parameter)
    {
        if (parameter is KeyValuePair<string, string> kvp)
        {
            ActiveFilterField = kvp.Key;
            ActiveFilterValue = kvp.Value;
        }
    }

    [RelayCommand]
    private void ClearFilter()
    {
        ActiveFilterField = null;
        ActiveFilterValue = null;
        MinLevel = AnyLevel;
        CorrelationFilter = null;
        ClearTimeFilter();
    }

    private void ApplyPropertyFilter(LogLineViewModel? line, string field)
    {
        var value = StructuredFieldResolver.Resolve(line?.Structured, field);
        if (string.IsNullOrEmpty(value))
        {
            StatusMessage = Loc.Format("Vm_Doc_NoProperty", field);
            return;
        }

        ActiveFilterField = field;
        ActiveFilterValue = value;
    }

    [RelayCommand]
    private void NextHighlight() => JumpTo(FindHighlight(forward: true));

    [RelayCommand]
    private void PreviousHighlight() => JumpTo(FindHighlight(forward: false));

    [RelayCommand]
    private void NextNewPattern() => JumpTo(FindIn(_newPatternLineNumbers, forward: true));

    [RelayCommand]
    private void PreviousNewPattern() => JumpTo(FindIn(_newPatternLineNumbers, forward: false));

    [RelayCommand]
    private void NextBookmark() => JumpTo(_bookmarks.Next(CurrentAnchorLineNumber()));

    [RelayCommand]
    private void PreviousBookmark() => JumpTo(_bookmarks.Previous(CurrentAnchorLineNumber()));

    private long? FindHighlight(bool forward) => FindIn(_highlightedLineNumbers, forward);

    private long? FindIn(SortedSet<long> lineNumbers, bool forward)
    {
        var anchor = CurrentAnchorLineNumber();

        if (forward)
        {
            var view = lineNumbers.GetViewBetween(anchor + 1, long.MaxValue);
            return view.Count > 0 ? view.Min : null;
        }

        if (anchor <= long.MinValue)
        {
            return null;
        }

        var below = lineNumbers.GetViewBetween(long.MinValue, anchor - 1);
        return below.Count > 0 ? below.Max : null;
    }

    private long CurrentAnchorLineNumber() => SelectedLine?.LineNumber ?? (Lines.Count > 0 ? Lines[^1].LineNumber : 0);

    private void JumpTo(long? lineNumber)
    {
        if (lineNumber is null)
        {
            return;
        }

        TryNavigateToLineNumber(lineNumber.Value);
    }

    /// <summary>Selects and scrolls to <paramref name="lineNumber"/> if it's still present in the live buffer.
    /// Used by search-result navigation, which may reference lines outside the bounded ring buffer.</summary>
    public bool TryNavigateToLineNumber(long lineNumber)
    {
        var target = Lines.FindByLineNumber(lineNumber);
        if (target is null)
        {
            return false;
        }

        SelectedLine = target;
        ScrollToLineRequested?.Invoke(target);
        return true;
    }

    // --- Time navigation: Δ column, go to time, time-range filter ---------------------------------

    /// <summary>Shows the "Δ" column: time since the previous timestamped line, or since
    /// <see cref="TimeReferenceLineNumber"/> when a reference line is set.</summary>
    [ObservableProperty]
    private bool _showTimeDelta;

    /// <summary>Line the Δ column measures from ("set as time reference"), or null to measure line-to-line.</summary>
    [ObservableProperty]
    private long? _timeReferenceLineNumber;

    private DateTimeOffset? _timeReferenceTimestamp;

    [ObservableProperty]
    private string? _goToTimeText;

    [ObservableProperty]
    private string? _timeFilterFromText;

    [ObservableProperty]
    private string? _timeFilterToText;

    /// <summary>Inclusive lower bound of the time-range filter, or null for unbounded.</summary>
    [ObservableProperty]
    private DateTimeOffset? _timeFilterFrom;

    /// <summary>Inclusive upper bound of the time-range filter, or null for unbounded.</summary>
    [ObservableProperty]
    private DateTimeOffset? _timeFilterTo;

    public bool IsTimeFilterActive => TimeFilterFrom is not null || TimeFilterTo is not null;

    public bool HasTimeReference => TimeReferenceLineNumber is not null;

    /// <summary>Timestamps are resolved lazily (a regex per line), only while a feature that needs them is on.</summary>
    private bool NeedsTimestamps => ShowTimeDelta || IsTimeFilterActive;

    partial void OnShowTimeDeltaChanged(bool value)
    {
        if (value)
        {
            ResolveTimestamps(Lines, recomputeDeltas: true);
            return;
        }

        foreach (var line in Lines)
        {
            line.DeltaDisplay = null;
        }
    }

    partial void OnTimeReferenceLineNumberChanged(long? value)
    {
        OnPropertyChanged(nameof(HasTimeReference));
        if (ShowTimeDelta)
        {
            ResolveTimestamps(Lines, recomputeDeltas: true);
        }
    }

    partial void OnTimeFilterFromChanged(DateTimeOffset? value) => OnTimeFilterChanged();

    partial void OnTimeFilterToChanged(DateTimeOffset? value) => OnTimeFilterChanged();

    private void OnTimeFilterChanged()
    {
        if (IsTimeFilterActive)
        {
            ResolveTimestamps(Lines, recomputeDeltas: false);
        }

        RaiseFilterChanged();
    }

    /// <summary>Resolves timestamps (and, when the Δ column is on, deltas) for lines about to be appended after
    /// the current last line, carrying the last known timestamp over for continuation lines.</summary>
    private void ResolveNewLines(IReadOnlyList<LogLineViewModel> items)
    {
        var previous = Lines.Count > 0 && Lines[^1].IsTimestampResolved ? Lines[^1].EffectiveTimestamp : null;
        ResolveTimestamps(items, recomputeDeltas: false, previous);
    }

    /// <summary>Walks <paramref name="items"/> in display order, resolving each unresolved line's own timestamp and
    /// its effective one (own, else inherited from the closest timestamped line above), and fills the Δ column.
    /// <paramref name="previousTimestamp"/> seeds the walk with the timestamp in effect just before the first item.</summary>
    private void ResolveTimestamps(IEnumerable<LogLineViewModel> items, bool recomputeDeltas, DateTimeOffset? previousTimestamp = null)
    {
        var reference = TimeReferenceLineNumber is not null ? _timeReferenceTimestamp : null;
        foreach (var line in items)
        {
            var wasResolved = line.IsTimestampResolved;
            if (!wasResolved)
            {
                var own = ResolveOwnTimestamp(line);
                line.SetResolvedTimestamp(own, own ?? previousTimestamp);
            }

            if (ShowTimeDelta && (!wasResolved || recomputeDeltas))
            {
                var anchor = reference ?? previousTimestamp;
                line.DeltaDisplay = line.Timestamp is { } ts && anchor is { } from ? TimeDeltaFormatter.Format(ts - from) : null;
            }

            previousTimestamp = line.EffectiveTimestamp;
        }
    }

    /// <summary>Structured timestamp when the line parsed; else the document's custom regex format (cheap, and it
    /// may know a date layout the generic extractor doesn't); else a timestamp pulled out of the raw text.</summary>
    private DateTimeOffset? ResolveOwnTimestamp(LogLineViewModel line)
    {
        if (line.Structured?.Timestamp is { } structured)
        {
            return structured;
        }

        var raw = TextForParsing(line.Text);
        if (!IsStructuredView && _lineParser is RegexLogLineParser && _lineParser.TryParse(raw, out var parsed) && parsed?.Timestamp is { } custom)
        {
            return custom;
        }

        return MergedTimestampExtractor.TryExtract(raw);
    }

    /// <summary>Newest own timestamp among the displayed lines — the reference for relative ("-5m") and
    /// time-of-day ("14:05") input.</summary>
    private DateTimeOffset? LatestTimestamp()
    {
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            if (Lines[i].Timestamp is { } ts)
            {
                return ts;
            }
        }

        return null;
    }

    private static string? FormatFilterTime(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether a line passes the time-range filter — true when no range is set. A line with no
    /// timestamp at all (not even an inherited one) can't be placed in time and is hidden while a range is active.</summary>
    public bool PassesTimeFilter(LogLineViewModel line)
    {
        if (!IsTimeFilterActive || !line.IsTimestampResolved)
        {
            return true;
        }

        return line.EffectiveTimestamp is { } ts
            && (TimeFilterFrom is not { } from || ts >= from)
            && (TimeFilterTo is not { } to || ts <= to);
    }

    /// <summary>Jumps to the first displayed line at or after the time typed in <see cref="GoToTimeText"/>. When the
    /// time is older than anything still in the ring buffer, hands off to the whole-file browser instead.</summary>
    [RelayCommand]
    private void GoToTime()
    {
        ResolveTimestamps(Lines, recomputeDeltas: false);
        if (!TimestampQuery.TryParse(GoToTimeText, LatestTimestamp(), out var target))
        {
            StatusMessage = Loc.Format("Vm_Doc_TimeInvalid", GoToTimeText ?? string.Empty);
            return;
        }

        var oldest = Lines.FirstOrDefault(l => l.Timestamp is not null)?.Timestamp;
        if (oldest is { } first && target < first && RequestFileBrowser(new FileBrowserTarget(null, target)))
        {
            StatusMessage = Loc.Get("Vm_Doc_TimeOutsideBuffer");
            return;
        }

        var match = Lines.FirstOrDefault(l => l.Timestamp is { } ts && ts >= target);
        if (match is null)
        {
            StatusMessage = Loc.Format("Vm_Doc_TimeNotFound", FormatFilterTime(target)!);
            return;
        }

        IsFollowingTail = false;
        SelectedLine = match;
        ScrollToLineRequested?.Invoke(match);
        StatusMessage = null;
    }

    [RelayCommand]
    private void ApplyTimeFilter()
    {
        ResolveTimestamps(Lines, recomputeDeltas: false);
        var reference = LatestTimestamp();

        DateTimeOffset? from = null;
        DateTimeOffset? to = null;
        if (!string.IsNullOrWhiteSpace(TimeFilterFromText))
        {
            if (!TimestampQuery.TryParse(TimeFilterFromText, reference, out var parsed))
            {
                StatusMessage = Loc.Format("Vm_Doc_TimeInvalid", TimeFilterFromText);
                return;
            }

            from = parsed;
        }

        if (!string.IsNullOrWhiteSpace(TimeFilterToText))
        {
            if (!TimestampQuery.TryParse(TimeFilterToText, reference, out var parsed))
            {
                StatusMessage = Loc.Format("Vm_Doc_TimeInvalid", TimeFilterToText);
                return;
            }

            to = parsed;
        }

        StatusMessage = null;
        TimeFilterFrom = from;
        TimeFilterTo = to;
    }

    [RelayCommand]
    private void ClearTimeFilter()
    {
        TimeFilterFromText = null;
        TimeFilterToText = null;
        TimeFilterFrom = null;
        TimeFilterTo = null;
    }

    /// <summary>Narrows the view to one timeline bucket (right-click on a timeline bar).</summary>
    [RelayCommand]
    private void FilterToBin(VolumeBin? bin)
    {
        if (bin is null)
        {
            return;
        }

        var to = bin.End - TimeSpan.FromTicks(1);
        TimeFilterFromText = FormatFilterTime(bin.Start);
        TimeFilterToText = FormatFilterTime(to);
        TimeFilterFrom = bin.Start;
        TimeFilterTo = to;
    }

    [RelayCommand]
    private void ToggleTimeDelta() => ShowTimeDelta = !ShowTimeDelta;

    /// <summary>Makes the Δ column measure from <paramref name="line"/> ("how long after this did X happen").</summary>
    [RelayCommand]
    private void SetTimeReference(LogLineViewModel? line)
    {
        if (line is null)
        {
            return;
        }

        ResolveTimestamps(Lines, recomputeDeltas: false);
        if ((line.Timestamp ?? line.EffectiveTimestamp) is not { } ts)
        {
            StatusMessage = Loc.Get("Vm_Doc_NoTimestamp");
            return;
        }

        _timeReferenceTimestamp = ts;
        TimeReferenceLineNumber = line.LineNumber;
        ShowTimeDelta = true;
        StatusMessage = Loc.Format("Vm_Doc_TimeReferenceSet", line.LineNumber);
    }

    [RelayCommand]
    private void ClearTimeReference()
    {
        _timeReferenceTimestamp = null;
        TimeReferenceLineNumber = null;
    }

    // --- Correlation-id filter ------------------------------------------------------------------

    /// <summary>When set, only lines containing this id (anywhere in the raw text — so it also works on plain-text
    /// and merged documents) are shown.</summary>
    [ObservableProperty]
    private CorrelationId? _correlationFilter;

    public bool IsCorrelationFilterActive => CorrelationFilter is not null;

    partial void OnCorrelationFilterChanged(CorrelationId? value) => RaiseFilterChanged();

    [RelayCommand]
    private void FilterByCorrelation(CorrelationId? id)
    {
        if (id is not null)
        {
            CorrelationFilter = id;
        }
    }

    [RelayCommand]
    private void ClearCorrelationFilter() => CorrelationFilter = null;

    /// <summary>Raised to apply a correlation filter to every open document (MainViewModel fans it out), for
    /// following one request/trace across the logs of several services at once.</summary>
    public event Action<CorrelationId>? CorrelationFilterAllRequested;

    [RelayCommand]
    private void FilterAllByCorrelation(CorrelationId? id)
    {
        if (id is not null)
        {
            CorrelationFilterAllRequested?.Invoke(id);
        }
    }

    public bool PassesCorrelationFilter(LogLineViewModel line) =>
        CorrelationFilter is not { } id || line.Text.Contains(id.Value, StringComparison.OrdinalIgnoreCase);

    // --- Named filter views ---------------------------------------------------------------------

    private bool _applyTimeFilterOnFirstLines;

    /// <summary>The app-wide saved filter views, for this document's "Filter views" menu.</summary>
    public IReadOnlyList<FilterView> FilterViews { get; private set; } = [];

    public bool HasFilterViews => FilterViews.Count > 0;

    public void ApplyFilterViews(IReadOnlyList<FilterView> views)
    {
        FilterViews = views;
        OnPropertyChanged(nameof(FilterViews));
        OnPropertyChanged(nameof(HasFilterViews));
    }

    /// <summary>Raised to save the current filters as a named view (MainViewModel prompts for the name).</summary>
    public event Action? SaveFilterViewRequested;

    public event Action<FilterView>? DeleteFilterViewRequested;

    [RelayCommand]
    private void SaveFilterView() => SaveFilterViewRequested?.Invoke();

    [RelayCommand]
    private void DeleteFilterView(FilterView? view)
    {
        if (view is not null)
        {
            DeleteFilterViewRequested?.Invoke(view);
        }
    }

    [RelayCommand]
    private void ApplyFilterView(FilterView? view)
    {
        if (view is null)
        {
            return;
        }

        ApplyFilters(view);
        StatusMessage = Loc.Format("Vm_FilterView_Applied", view.Name);
    }

    /// <summary>Snapshots every active display filter into a <see cref="FilterView"/> named <paramref name="name"/>.
    /// Time bounds keep the text the user typed, so relative ranges stay relative.</summary>
    public FilterView CaptureFilters(string name) => new()
    {
        Name = name,
        TextFilterPattern = string.IsNullOrEmpty(TextFilterPattern) ? null : TextFilterPattern,
        TextFilterIsRegex = TextFilterIsRegex,
        TextFilterCaseSensitive = TextFilterCaseSensitive,
        TextFilterExclude = TextFilterExclude,
        MinLevel = IsLevelFilterActive ? MinLevel : null,
        PropertyFilterField = ActiveFilterValue is null ? null : ActiveFilterField,
        PropertyFilterValue = ActiveFilterValue,
        CorrelationName = CorrelationFilter?.Name,
        CorrelationValue = CorrelationFilter?.Value,
        TimeFilterFromText = IsTimeFilterActive && TimeFilterFrom is not null ? TimeFilterFromText : null,
        TimeFilterToText = IsTimeFilterActive && TimeFilterTo is not null ? TimeFilterToText : null,
    };

    /// <summary>Replaces this document's whole filter set with <paramref name="view"/>. A time range is applied
    /// immediately when lines are present, otherwise as soon as the first lines arrive (session restore).</summary>
    public void ApplyFilters(FilterView view)
    {
        TextFilterIsRegex = view.TextFilterIsRegex;
        TextFilterCaseSensitive = view.TextFilterCaseSensitive;
        TextFilterExclude = view.TextFilterExclude;
        TextFilterPattern = view.TextFilterPattern;
        MinLevel = string.IsNullOrEmpty(view.MinLevel) ? AnyLevel : view.MinLevel;
        ActiveFilterField = view.PropertyFilterValue is null ? null : view.PropertyFilterField;
        ActiveFilterValue = view.PropertyFilterValue;
        CorrelationFilter = string.IsNullOrEmpty(view.CorrelationValue) ? null : new CorrelationId(view.CorrelationName ?? "ID", view.CorrelationValue);

        TimeFilterFromText = view.TimeFilterFromText;
        TimeFilterToText = view.TimeFilterToText;
        if (string.IsNullOrWhiteSpace(view.TimeFilterFromText) && string.IsNullOrWhiteSpace(view.TimeFilterToText))
        {
            _applyTimeFilterOnFirstLines = false;
            TimeFilterFrom = null;
            TimeFilterTo = null;
        }
        else if (Lines.Any(l => l.LineNumber > 0))
        {
            ApplyTimeFilter();
        }
        else
        {
            _applyTimeFilterOnFirstLines = true;
        }
    }

    // --- Exceptions panel and whole-file browser --------------------------------------------------

    /// <summary>Raised to open the grouped-exceptions panel over this document.</summary>
    public event Action? ExceptionGroupsRequested;

    [RelayCommand]
    private void ShowExceptionGroups() => ExceptionGroupsRequested?.Invoke();

    /// <summary>Raised to open the virtualized whole-file browser, optionally positioned at a line or a time.</summary>
    public event Action<FileBrowserTarget?>? FileBrowserRequested;

    [RelayCommand]
    private void BrowseWholeFile()
    {
        if (!RequestFileBrowser(SelectedLine is { LineNumber: > 0 } line ? new FileBrowserTarget(line.LineNumber, null) : null))
        {
            StatusMessage = Loc.Get("Vm_Doc_NotFileBacked");
        }
    }

    /// <summary>Opens the whole-file browser when this document is file-backed; returns false (doing nothing)
    /// otherwise, so callers can fall back to a status message.</summary>
    public bool RequestFileBrowser(FileBrowserTarget? target)
    {
        if (SearchableFilePath is null)
        {
            return false;
        }

        FileBrowserRequested?.Invoke(target);
        return true;
    }

    public void Dispose()
    {
        LogLineParsers.CustomFormatsChanged -= OnCustomFormatsChanged;
        _reprocessCts?.Cancel();
        _timelineRecomputeTimer?.Stop();
        _sink.LinesFlushed -= OnLinesFlushed;
        _sink.ResetFlushed -= OnResetFlushed;
        _sink.Dispose();
        _source.Dispose();
    }
}
