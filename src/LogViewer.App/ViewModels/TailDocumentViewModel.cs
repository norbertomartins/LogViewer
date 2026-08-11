using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Controls;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.Bookmarks;
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
    private readonly HighlightEngine _highlightEngine = new();
    private readonly BookmarkManager _bookmarks = new();
    private readonly SortedSet<long> _highlightedLineNumbers = new();
    private readonly UiDispatcherLineSink _sink;
    private readonly Dictionary<Guid, DateTime> _lastAutoTriggerAt = new();

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

    public bool IsFilterActive => ActiveFilterValue is not null || IsLevelFilterActive;

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
        bool isStructuredView = false)
    {
        _source = source;
        SourcePath = sourcePath;
        _buffer = new RingLineBuffer(ringBufferCapacity);
        Lines = new DisplayLineCollection(ringBufferCapacity);
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

    partial void OnIsStructuredViewChanged(bool value) => ReprocessAllLines();

    /// <summary>Rebuilds every displayed line from its raw text when <see cref="IsStructuredView"/> is toggled —
    /// unlike highlight colors, <see cref="LogLineViewModel.Structured"/> isn't a mutable per-line property, so
    /// the display items themselves need replacing rather than just re-evaluated in place.</summary>
    private void ReprocessAllLines()
    {
        var selectedLineNumber = SelectedLine?.LineNumber;
        var rebuilt = new List<LogLineViewModel>(Lines.Count);
        _highlightedLineNumbers.Clear();

        foreach (var existing in Lines)
        {
            var structured = IsStructuredView && SerilogEventParser.TryParse(existing.Text, out var parsed) ? parsed : null;
            var match = _highlightEngine.Evaluate(existing.Text, structured);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(existing.LineNumber);
            }

            rebuilt.Add(new LogLineViewModel(existing.LineNumber, existing.Text, structured, match, existing.IsBookmarked));
        }

        Lines.Clear();
        Lines.AppendRange(rebuilt);

        SelectedLine = selectedLineNumber is { } lineNumber ? Lines.FindByLineNumber(lineNumber) : null;
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
            var structured = IsStructuredView && SerilogEventParser.TryParse(line.Text, out var parsed) ? parsed : null;
            var match = _highlightEngine.Evaluate(line.Text, structured);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
                TryAutoTriggerExternalTools(match.RuleId, line);
            }

            displayItems.Add(new LogLineViewModel(line.LineNumber, line.Text, structured, match, _bookmarks.IsBookmarked(line.LineNumber)));
        }

        _buffer.AppendRange(lines);
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

    /// <summary>Every currently displayed line that parsed as a structured event, in display order — the
    /// pool <see cref="Core.BlockDiff.LogBlockExtractor"/> extracts the anchor block from.</summary>
    public IReadOnlyList<(long LineNumber, StructuredLogEvent Event)> StructuredLines =>
        Lines.Where(l => l.Structured is not null).Select(l => (l.LineNumber, l.Structured!)).ToList();

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
        _sink.LinesFlushed -= OnLinesFlushed;
        _sink.ResetFlushed -= OnResetFlushed;
        _sink.Dispose();
        _source.Dispose();
    }
}
