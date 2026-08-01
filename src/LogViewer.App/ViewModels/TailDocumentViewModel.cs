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
    private bool _hasUnseenChanges;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private LogLineViewModel? _selectedLine;

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
        IReadOnlyList<HighlightRule> highlightRules,
        IReadOnlyList<ExternalToolDefinition> externalTools,
        int ringBufferCapacity,
        TimeSpan uiRefreshInterval,
        string? title = null,
        string? eventLogChannelName = null,
        IReadOnlyList<EventLogFilterRule>? eventLogFilters = null)
    {
        _source = source;
        SourcePath = sourcePath;
        _buffer = new RingLineBuffer(ringBufferCapacity);
        Lines = new DisplayLineCollection(ringBufferCapacity);
        _highlightEngine.SetRules(highlightRules);
        _externalTools = externalTools;
        _title = title ?? (Path.GetFileName(sourcePath) is { Length: > 0 } fileName ? fileName : source.DisplayName);

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

    /// <summary>Applies updated highlight rules, live-recoloring every currently displayed line to match.</summary>
    public void ApplyHighlightRules(IReadOnlyList<HighlightRule> rules)
    {
        _highlightEngine.SetRules(rules);
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
            var match = _highlightEngine.Evaluate(line.Text);
            line.ApplyMatch(match);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
            }
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
            var match = _highlightEngine.Evaluate(line.Text);
            if (match is not null)
            {
                _highlightedLineNumbers.Add(line.LineNumber);
                TryAutoTriggerExternalTools(match.RuleId, line);
            }

            displayItems.Add(new LogLineViewModel(line.LineNumber, line.Text, match, _bookmarks.IsBookmarked(line.LineNumber)));
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

        var marker = new LogLineViewModel(0, $"── file {reason.ToString().ToLowerInvariant()} — resuming ──", match: null, isBookmarked: false);
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
