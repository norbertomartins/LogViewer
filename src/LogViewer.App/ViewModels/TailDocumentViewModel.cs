using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Controls;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.Analysis;
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
    private readonly RingLineBuffer _buffer;
    private ILogLineParser _lineParser;
    private readonly HighlightEngine _highlightEngine = new();
    private readonly BookmarkManager _bookmarks = new();
    private readonly SortedSet<long> _highlightedLineNumbers = new();
    private readonly UiDispatcherLineSink _sink;
    private readonly Dictionary<Guid, DateTime> _lastAutoTriggerAt = new();
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

    [ObservableProperty]
    private bool _isStructuredView;

    [ObservableProperty]
    private bool _isColorizeStructuredValues = true;

    /// <summary>When true, the matched sub-string(s) of a highlight rule are emphasized within the line
    /// (bold + underline). Synced from the global setting via <see cref="ApplyShowHighlightMatchSpans"/>.</summary>
    [ObservableProperty]
    private bool _showHighlightMatchSpans = true;

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

    public bool IsFilterActive => ActiveFilterValue is not null || IsLevelFilterActive || IsTextFilterActive;

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

    partial void OnTextFilterPatternChanged(string? value) => RebuildTextFilter();

    partial void OnTextFilterExcludeChanged(bool value) => RaiseFilterChanged();

    partial void OnTextFilterIsRegexChanged(bool value) => RebuildTextFilter();

    partial void OnTextFilterCaseSensitiveChanged(bool value) => RebuildTextFilter();

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
                StatusMessage = $"Invalid filter regex: {ex.Message}";
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

    /// <summary>Raised by <see cref="ExportVisibleCommand"/> — the view handles it because the effective
    /// (filtered) line set lives in its <c>ICollectionView</c>, not in the view-model.</summary>
    public event Action? ExportRequested;

    [RelayCommand]
    private void ExportVisible() => ExportRequested?.Invoke();

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
                parts.Add($"Level ≥ {MinLevel}");
            }

            if (IsTextFilterActive)
            {
                parts.Add($"text {(TextFilterExclude ? "≠" : "~")} \"{TextFilterPattern}\"");
            }

            return parts.Count > 0 ? "Filtered by " + string.Join(" AND ", parts) : null;
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
        _highlightEngine.SetRules(HighlightPreset.FlattenForMatching(highlightPresets));
        _externalTools = externalTools;
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
        _source.SourceReset += (_, e) => _sink.EnqueueReset(e.Reason);
        _source.Error += (_, e) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusMessage = e.Exception.Message);

        _source.Start();
    }

    public string SourcePath { get; }

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


    /// <summary>Format ids offered in the picker, in detection-priority order.</summary>
    public IReadOnlyList<string> AvailableStructuredFormats { get; } = LogLineParsers.FormatIds;

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
        _ => TailSourceKind.EventLog,
    };

    /// <summary>The EventLog channel + filters a full-channel search should scan, or null for file-backed documents.</summary>
    public (string Channel, IReadOnlyList<EventLogFilterRule> Filters)? SearchableEventLog { get; }

    public event Action? SearchRequested;

    public event Action? CustomizeRequested;

    [RelayCommand]
    private void Search() => SearchRequested?.Invoke();

    [RelayCommand]
    private void Customize() => CustomizeRequested?.Invoke();

    /// <summary>Applies an updated external-tool set for the "Run Tool" toolbar menu and auto-trigger matching.</summary>
    public void ApplyExternalTools(IReadOnlyList<ExternalToolDefinition> tools)
    {
        _externalTools = tools;
        OnPropertyChanged(nameof(ExternalTools));
    }

    /// <summary>Syncs the global colorize-structured-values setting to this document's own toggle.</summary>
    public void ApplyColorizeStructuredValues(bool colorize) => IsColorizeStructuredValues = colorize;

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

    /// <summary>Total lines ever appended (not bounded by the ring buffer), used for the title-bar lines/sec stat.</summary>
    public long TotalLinesAppended => _buffer.TotalLinesAppended;

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
        _highlightEngine.SetRules(HighlightPreset.FlattenForMatching(presets));
        ReapplyHighlighting();
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
            StatusMessage = $"Failed to switch structured view: {ex.Message}";
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
            var snapshot = Lines.ToList();
            var rebuilt = new List<LogLineViewModel>(snapshot.Count);
            var highlighted = new SortedSet<long>();

            for (var i = 0; i < snapshot.Count; i++)
            {
                var existing = snapshot[i];
                var structured = isStructuredView && _lineParser.TryParse(existing.Text, out var parsed) ? parsed : null;
                var match = _highlightEngine.Evaluate(existing.Text, structured);
                if (match is not null)
                {
                    highlighted.Add(existing.LineNumber);
                }

                rebuilt.Add(new LogLineViewModel(existing.LineNumber, existing.Text, structured, match, existing.IsBookmarked));

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

            Lines.Clear();
            Lines.AppendRange(rebuilt);
            if (_pendingDuringReprocess.Count > 0)
            {
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

    public event Action? ScrollToEndRequested;

    public event Action<LogLineViewModel>? ScrollToLineRequested;

    private void OnLinesFlushed(IReadOnlyList<TailLine> lines)
    {
        var displayItems = new List<LogLineViewModel>(lines.Count);
        foreach (var line in lines)
        {
            var structured = IsStructuredView && _lineParser.TryParse(line.Text, out var parsed) ? parsed : null;
            var match = _highlightEngine.Evaluate(line.Text, structured);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
                TryAutoTriggerExternalTools(match.RuleId, line);
            }

            displayItems.Add(new LogLineViewModel(line.LineNumber, line.Text, structured, match, _bookmarks.IsBookmarked(line.LineNumber)));
        }

        _buffer.AppendRange(lines);

        if (_isReprocessing)
        {
            // ReprocessAllLinesAsync took a snapshot of Lines and will replace it wholesale when done —
            // append there directly and it either gets clobbered or duplicated. Queue instead; the
            // reprocess appends this queue onto its rebuilt list once it finishes.
            _pendingDuringReprocess.AddRange(displayItems);
            return;
        }

        Lines.AppendRange(displayItems);
        TrimEvictedLineNumbers();

        if (IsFollowingTail)
        {
            ScrollToEndRequested?.Invoke();
        }
        else
        {
            HasUnseenChanges = true;
        }
    }

    private void OnResetFlushed(TailResetReason reason)
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

        var marker = new LogLineViewModel(0, $"── file {reason.ToString().ToLowerInvariant()} — resuming ──", structured: null, match: null, isBookmarked: false);
        Lines.AppendRange([marker]);
        StatusMessage = $"Source {reason.ToString().ToLowerInvariant()} — resumed tailing.";
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
            StatusMessage = "Selected line is not a structured log event.";
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
            if (line.Structured?.Timestamp is { } ts)
            {
                var severity = LogLevelSeverity.Rank(line.Structured.Level) ?? 2;
                samples.Add(new VolumeSample(ts, severity, line.LineNumber));
            }
        }

        TimelineHasData = samples.Count >= 2;
        var bins = LogVolumeBinner.Bin(samples);

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
    }

    private void ApplyPropertyFilter(LogLineViewModel? line, string field)
    {
        var value = StructuredFieldResolver.Resolve(line?.Structured, field);
        if (string.IsNullOrEmpty(value))
        {
            StatusMessage = $"Selected line has no {field} property.";
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
    private void NextBookmark() => JumpTo(_bookmarks.Next(CurrentAnchorLineNumber()));

    [RelayCommand]
    private void PreviousBookmark() => JumpTo(_bookmarks.Previous(CurrentAnchorLineNumber()));

    private long? FindHighlight(bool forward)
    {
        var anchor = CurrentAnchorLineNumber();

        if (forward)
        {
            var view = _highlightedLineNumbers.GetViewBetween(anchor + 1, long.MaxValue);
            return view.Count > 0 ? view.Min : null;
        }

        if (anchor <= long.MinValue)
        {
            return null;
        }

        var below = _highlightedLineNumbers.GetViewBetween(long.MinValue, anchor - 1);
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

    public void Dispose()
    {
        _reprocessCts?.Cancel();
        _timelineRecomputeTimer?.Stop();
        _sink.LinesFlushed -= OnLinesFlushed;
        _sink.ResetFlushed -= OnResetFlushed;
        _sink.Dispose();
        _source.Dispose();
    }
}
