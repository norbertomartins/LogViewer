using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Controls;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.Core.Analysis;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Indexing;
using LogViewer.Core.Search;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;
using LogViewer.Core.Theming;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal "whole file" browser: every line of a (possibly multi-GB) file, beyond what the live
/// document's ring buffer retains, browsed through a <see cref="FileLineIndex"/> + <see cref="VirtualFileLineList"/>
/// so memory stays flat regardless of file size. Indexes in the background with progress, keeps indexing new
/// bytes while the file grows, and supports go-to-line and go-to-time (binary search over the index).
/// </summary>
public sealed partial class FileBrowserViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

    private readonly FileLineIndex _index;
    private readonly HighlightEngine _highlightEngine = new();
    private readonly Func<long, bool>? _showInDocument;
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly string? _formatId;
    private FileBrowserTarget? _pendingTarget;
    private CancellationTokenSource? _searchCts;

    public FileBrowserViewModel(
        string filePath,
        string title,
        IReadOnlyList<HighlightPreset> highlightPresets,
        ThemeBaseMode themeMode,
        double logFontSize,
        FileBrowserTarget? initialTarget,
        Func<long, bool>? showInDocument,
        string? structuredFormatId = null)
    {
        FilePath = filePath;
        Title = title;
        LogFontSize = logFontSize;
        _showInDocument = showInDocument;
        _pendingTarget = initialTarget;
        _formatId = structuredFormatId;

        _highlightEngine.SetRules(HighlightPreset.FlattenForMatching(highlightPresets).ToList());
        _highlightEngine.SetThemeMode(themeMode);

        _index = new FileLineIndex(filePath);
        Lines = new VirtualFileLineList(_index, text => _highlightEngine.Evaluate(text, null));

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    public string FilePath { get; }

    public string Title { get; }

    public double LogFontSize { get; }

    public VirtualFileLineList Lines { get; }

    [ObservableProperty]
    private bool _isIndexing;

    [ObservableProperty]
    private double _indexProgress;

    [ObservableProperty]
    private long _totalLines;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _goToLineText;

    [ObservableProperty]
    private string? _goToTimeText;

    /// <summary>Keep the view pinned to the last line as the file grows.</summary>
    [ObservableProperty]
    private bool _followEnd;

    [ObservableProperty]
    private LogLineViewModel? _selectedLine;

    /// <summary>Raised with the 0-based list index the view should scroll to (and select).</summary>
    public event Action<int>? ScrollToIndexRequested;

    /// <summary>Builds the index (with progress), then positions the view at the initial target and starts
    /// following the file's growth.</summary>
    public async Task InitializeAsync()
    {
        IsIndexing = true;
        StatusMessage = Loc.Get("Vm_Browser_Indexing");
        try
        {
            await _index.UpdateAsync(new Progress<double>(p => IndexProgress = p), _cts.Token);
            Lines.Refresh(contentChanged: true);
            TotalLines = _index.LineCount;
            StatusMessage = null;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.Format("Vm_Browser_Failed", ex.Message);
            return;
        }
        finally
        {
            IsIndexing = false;
        }

        var target = _pendingTarget;
        _pendingTarget = null;
        if (target?.Time is { } time)
        {
            await GoToTimestampAsync(time);
        }
        else if (target?.LineNumber is { } line)
        {
            NavigateToLine(line);
        }
        else
        {
            NavigateToLine(TotalLines);
        }

        _refreshTimer.Start();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var update = await _index.UpdateAsync(cancellationToken: _cts.Token);
            if (update == LineIndexUpdate.Unchanged)
            {
                return;
            }

            Lines.Refresh(contentChanged: update == LineIndexUpdate.Rebuilt);
            TotalLines = _index.LineCount;
            if (FollowEnd)
            {
                NavigateToLine(TotalLines);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.Format("Vm_Browser_Failed", ex.Message);
        }
    }

    public void NavigateToLine(long lineNumber)
    {
        if (Lines.Count == 0)
        {
            return;
        }

        var index = (int)Math.Clamp(lineNumber - 1, 0, Lines.Count - 1);
        SelectedLine = Lines[index];
        ScrollToIndexRequested?.Invoke(index);
    }

    [RelayCommand]
    private void GoToLine()
    {
        if (!long.TryParse(GoToLineText?.Trim(), out var line) || line < 1)
        {
            StatusMessage = Loc.Format("Vm_Browser_InvalidLine", GoToLineText ?? string.Empty);
            return;
        }

        FollowEnd = false;
        StatusMessage = line > TotalLines ? Loc.Format("Vm_Browser_LineClamped", TotalLines) : null;
        NavigateToLine(line);
    }

    [RelayCommand]
    private async Task GoToTimeAsync()
    {
        var reference = LastTimestamp();
        if (!TimestampQuery.TryParse(GoToTimeText, reference, out var target))
        {
            StatusMessage = Loc.Format("Vm_Doc_TimeInvalid", GoToTimeText ?? string.Empty);
            return;
        }

        FollowEnd = false;
        await GoToTimestampAsync(target);
    }

    private async Task GoToTimestampAsync(DateTimeOffset target)
    {
        StatusMessage = Loc.Get("Vm_Browser_Searching");
        var extract = CreateTimestampExtractor();
        try
        {
            var line = await Task.Run(() => _index.FindFirstLineAtOrAfter(target, extract, _cts.Token), _cts.Token);
            if (line is null)
            {
                StatusMessage = Loc.Format("Vm_Doc_TimeNotFound", target.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
                return;
            }

            StatusMessage = null;
            NavigateToLine(line.Value);
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Timestamp of the last timestamped line near the end of the file — the reference for relative and
    /// time-of-day input, matching how the live document interprets the same text.</summary>
    private DateTimeOffset? LastTimestamp()
    {
        var extract = CreateTimestampExtractor();
        var from = Math.Max(1, TotalLines - 200);
        return _index.ReadLines(from, 201).Select(l => extract(l.Text)).LastOrDefault(ts => ts is not null);
    }

    /// <summary>Parsed timestamp for the document's structured format when there is one (it may know a date layout
    /// the generic extractor doesn't), else a timestamp pulled from the raw text. A fresh parser per call: the
    /// search runs on a background thread and parsers aren't required to be thread-safe.</summary>
    private Func<string, DateTimeOffset?> CreateTimestampExtractor()
    {
        var parser = LogLineParsers.Create(_formatId);
        return text => parser is not null && parser.TryParse(text, out var evt) && evt?.Timestamp is { } ts
            ? ts
            : MergedTimestampExtractor.TryExtract(text);
    }

    // --- Search within the whole file -------------------------------------------------------------

    [ObservableProperty]
    private string? _searchText;

    [ObservableProperty]
    private bool _searchIsRegex;

    [ObservableProperty]
    private bool _searchCaseSensitive;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private double _searchProgress;

    /// <summary>"N matches" after a full count, cleared whenever the search text/options change.</summary>
    [ObservableProperty]
    private string? _matchCountText;

    partial void OnSearchTextChanged(string? value) => MatchCountText = null;

    partial void OnSearchIsRegexChanged(bool value) => MatchCountText = null;

    partial void OnSearchCaseSensitiveChanged(bool value) => MatchCountText = null;

    [RelayCommand]
    private Task FindNextAsync() => FindAsync(forward: true);

    [RelayCommand]
    private Task FindPreviousAsync() => FindAsync(forward: false);

    /// <summary>Scans from the selected line (or the start/end) to the next/previous match, wrapping around the
    /// file once. Runs on a background thread over index pages so a miss in a multi-GB file stays responsive and
    /// cancellable; starting another search cancels the running one.</summary>
    private async Task FindAsync(bool forward)
    {
        if (!TryCreateMatcher(out var isMatch))
        {
            return;
        }

        var cts = RestartSearch();
        var from = SelectedLine?.LineNumber ?? (forward ? 0 : TotalLines + 1);
        var progress = new Progress<double>(p => SearchProgress = p);
        IsSearching = true;
        StatusMessage = Loc.Get("Vm_Browser_Searching");
        try
        {
            var hit = await Task.Run(() => _index.FindNext(from, forward, isMatch, progress, cts.Token), cts.Token);
            var wrapped = false;
            if (hit is null)
            {
                wrapped = true;
                var restart = forward ? 0 : TotalLines + 1;
                hit = await Task.Run(() => _index.FindNext(restart, forward, isMatch, progress, cts.Token), cts.Token);
            }

            if (hit is not { } found)
            {
                StatusMessage = Loc.Format("Vm_Browser_NoMatch", SearchText ?? string.Empty);
                return;
            }

            FollowEnd = false;
            NavigateToLine(found.LineNumber);
            StatusMessage = wrapped ? Loc.Get(forward ? "Vm_Browser_WrappedToStart" : "Vm_Browser_WrappedToEnd") : null;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = null;
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private async Task CountMatchesAsync()
    {
        if (!TryCreateMatcher(out var isMatch))
        {
            return;
        }

        var cts = RestartSearch();
        var progress = new Progress<double>(p => SearchProgress = p);
        IsSearching = true;
        try
        {
            var count = await Task.Run(() => _index.CountMatches(isMatch, progress, cts.Token), cts.Token);
            MatchCountText = Loc.Format("Vm_Browser_MatchCount", count);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                IsSearching = false;
            }
        }
    }

    [RelayCommand]
    private void CancelSearch() => _searchCts?.Cancel();

    private bool TryCreateMatcher(out Func<string, bool> isMatch)
    {
        if (LineMatcher.TryCreate(SearchText, SearchIsRegex, SearchCaseSensitive, out isMatch, out var error))
        {
            return true;
        }

        StatusMessage = string.IsNullOrEmpty(SearchText) ? null : Loc.Format("Vm_Doc_InvalidFilterRegex", error ?? string.Empty);
        return false;
    }

    private CancellationTokenSource RestartSearch()
    {
        _searchCts?.Cancel();
        _searchCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        SearchProgress = 0;
        return _searchCts;
    }

    /// <summary>Jumps the live document to the selected line when it's still inside the document's ring buffer.</summary>
    [RelayCommand]
    private void ShowInDocument()
    {
        if (SelectedLine is null || _showInDocument is null)
        {
            return;
        }

        StatusMessage = _showInDocument(SelectedLine.LineNumber) ? null : Loc.Get("Vm_Browser_NotInBuffer");
    }

    [RelayCommand]
    private void CopySelected()
    {
        if (SelectedLine is null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(SelectedLine.Text);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _searchCts?.Cancel();
        _cts.Cancel();
    }
}
