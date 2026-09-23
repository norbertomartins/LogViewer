using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.App.ViewModels;

/// <summary>One structured event as a grid row; <see cref="Values"/> follows <see cref="StructuredGridViewModel.VisiblePropertyColumns"/>.</summary>
public sealed record StructuredGridRow(long LineNumber, StructuredLogEvent Event)
{
    public DateTimeOffset? Timestamp => Event.Timestamp;

    public string? Level => Event.Level;

    public string Message => Event.RenderedMessage;

    public IReadOnlyList<string?> Values { get; init; } = [];
}

/// <summary>A property offered as a grid column, with how many of the events carry it.</summary>
public sealed partial class StructuredColumnChoice(string name, int count, bool isVisible) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private int _count = count;

    [ObservableProperty]
    private bool _isVisible = isVisible;
}

/// <summary>
/// Backs the non-modal "Column view": the document's structured events (parsed with its format even when the
/// document itself shows raw lines) as a table — line, time, level, the chosen properties and the message — sortable by
/// any column (numbers numerically) and filterable by cell value or free text. Like the exceptions panel it covers the
/// lines in the buffer and, while live, refreshes as new lines arrive; a row jumps to its line in the document.
/// </summary>
public sealed partial class StructuredGridViewModel : ObservableObject, IDisposable
{
    public const string LineColumn = "#Line";
    public const string TimeColumn = "@Timestamp";
    public const string LevelColumn = StructuredFieldResolver.LevelField;
    public const string MessageColumn = StructuredFieldResolver.MessageField;

    /// <summary>How many of the most common properties are shown until the user picks columns.</summary>
    public const int DefaultPropertyColumnCount = 4;

    private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly TailDocumentViewModel _document;
    private readonly DispatcherTimer _liveRefreshTimer;
    private CancellationTokenSource? _refreshCts;
    private List<StructuredGridRow> _allRows = [];
    private bool _userChoseColumns;
    private bool _updatingColumns;

    public StructuredGridViewModel(TailDocumentViewModel document)
    {
        _document = document;
        _liveRefreshTimer = new DispatcherTimer { Interval = LiveRefreshInterval };
        _liveRefreshTimer.Tick += (_, _) =>
        {
            _liveRefreshTimer.Stop();
            _ = RefreshAsync();
        };
        _document.Lines.CollectionChanged += OnDocumentLinesChanged;
        Filters.CollectionChanged += (_, _) => ApplyView();

        _ = RefreshAsync();
    }

    public string Title => _document.Title;

    /// <summary>When on (the default), the grid reloads as new lines are tailed.</summary>
    [ObservableProperty]
    private bool _isLive = true;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Every property found in the events, most common first; ticking one shows it as a column.</summary>
    public ObservableCollection<StructuredColumnChoice> PropertyColumns { get; } = [];

    public IReadOnlyList<string> VisiblePropertyColumns { get; private set; } = [];

    /// <summary>Raised when the set of property columns changes, so the view rebuilds its grid columns.</summary>
    public event Action? ColumnsChanged;

    /// <summary>The filtered, sorted rows the grid shows (replaced as a whole on every change).</summary>
    [ObservableProperty]
    private IReadOnlyList<StructuredGridRow> _rows = [];

    [ObservableProperty]
    private StructuredGridRow? _selectedRow;

    /// <summary>Value filters from the cells' context menu; all must match.</summary>
    public ObservableCollection<StructuredValueFilter> Filters { get; } = [];

    /// <summary>Keeps rows whose level, message or a shown property contains this text (case-insensitive).</summary>
    [ObservableProperty]
    private string? _searchText;

    public string SortColumn { get; private set; } = LineColumn;

    public bool SortDescending { get; private set; }

    partial void OnSearchTextChanged(string? value) => ApplyView();

    private void OnDocumentLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsLive && !_liveRefreshTimer.IsEnabled)
        {
            _liveRefreshTimer.Start();
        }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _refreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _refreshCts = cts;
        IsLoading = true;

        try
        {
            // Snapshot on the UI thread; parsing runs in the background.
            var snapshot = _document.Lines.Select(l => (l.LineNumber, l.Text, l.Structured)).ToList();
            var formatId = _document.StructuredFormatId;
            var events = await Task.Run(() => Parse(snapshot, formatId, cts.Token), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            UpdateColumns(StructuredColumns.DiscoverProperties(events.Select(e => e.Event)));
            _allRows = [.. events.Select(WithValues)];
            ApplyView();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                IsLoading = false;
            }
        }
    }

    private static List<StructuredGridRow> Parse(
        IReadOnlyList<(long LineNumber, string Text, StructuredLogEvent? Structured)> lines, string formatId, CancellationToken token)
    {
        var parser = LogLineParsers.Create(formatId);
        var rows = new List<StructuredGridRow>();
        foreach (var (lineNumber, text, structured) in lines)
        {
            token.ThrowIfCancellationRequested();
            if (lineNumber <= 0)
            {
                continue; // reset/switch marker rows
            }

            var evt = structured ?? (parser is not null && parser.TryParse(MergedTailSource.StripLabel(text), out var parsed) ? parsed : null);
            if (evt is not null)
            {
                rows.Add(new StructuredGridRow(lineNumber, evt));
            }
        }

        return rows;
    }

    /// <summary>Merges newly discovered properties into <see cref="PropertyColumns"/>, keeping the user's ticks.</summary>
    private void UpdateColumns(IReadOnlyList<StructuredPropertyUsage> usage)
    {
        _updatingColumns = true;
        try
        {
            var existing = PropertyColumns.ToDictionary(c => c.Name, StringComparer.Ordinal);
            for (var i = 0; i < usage.Count; i++)
            {
                if (existing.TryGetValue(usage[i].Name, out var column))
                {
                    column.Count = usage[i].Count;
                    if (!_userChoseColumns)
                    {
                        column.IsVisible = i < DefaultPropertyColumnCount;
                    }
                }
                else
                {
                    column = new StructuredColumnChoice(usage[i].Name, usage[i].Count, !_userChoseColumns && i < DefaultPropertyColumnCount);
                    column.PropertyChanged += OnColumnChoiceChanged;
                    PropertyColumns.Add(column);
                }
            }

            // Most common first, like the discovery order (a stable sort keeps equal counts where they were).
            var ordered = PropertyColumns.OrderByDescending(c => c.Count).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                var from = PropertyColumns.IndexOf(ordered[i]);
                if (from != i)
                {
                    PropertyColumns.Move(from, i);
                }
            }
        }
        finally
        {
            _updatingColumns = false;
        }

        SyncVisibleColumns();
    }

    private void OnColumnChoiceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingColumns || e.PropertyName != nameof(StructuredColumnChoice.IsVisible))
        {
            return;
        }

        _userChoseColumns = true;
        if (SyncVisibleColumns())
        {
            _allRows = [.. _allRows.Select(WithValues)];
            ApplyView();
        }
    }

    /// <summary>Recomputes <see cref="VisiblePropertyColumns"/>; true (and <see cref="ColumnsChanged"/> raised) when it changed.</summary>
    private bool SyncVisibleColumns()
    {
        var visible = PropertyColumns.Where(c => c.IsVisible).Select(c => c.Name).ToList();
        if (visible.SequenceEqual(VisiblePropertyColumns, StringComparer.Ordinal))
        {
            return false;
        }

        VisiblePropertyColumns = visible;
        if (SortColumn is not (LineColumn or TimeColumn or LevelColumn or MessageColumn) && !visible.Contains(SortColumn))
        {
            SortColumn = LineColumn;
            SortDescending = false;
        }

        ColumnsChanged?.Invoke();
        return true;
    }

    private StructuredGridRow WithValues(StructuredGridRow row) =>
        row with { Values = [.. VisiblePropertyColumns.Select(name => row.Event.Properties.GetValueOrDefault(name))] };

    /// <summary>The text of <paramref name="row"/>'s cell in <paramref name="column"/> (a column key or property name).</summary>
    public static string? CellValue(StructuredGridRow row, string column) => column switch
    {
        LineColumn => row.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
        TimeColumn => row.Timestamp?.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => StructuredFieldResolver.Resolve(row.Event, column),
    };

    /// <summary>Sorts by <paramref name="column"/>, or flips the direction when it already is the sort column.</summary>
    public void SortBy(string column)
    {
        SortDescending = SortColumn == column && !SortDescending;
        SortColumn = column;
        ApplyView();
    }

    /// <summary>Filters the grid to (or, with <paramref name="exclude"/>, away from) rows whose <paramref name="column"/>
    /// has <paramref name="value"/>. Line numbers and times are unique per row, so they aren't filterable.</summary>
    public void AddFilter(string column, string? value, bool exclude)
    {
        if (column is LineColumn or TimeColumn)
        {
            return;
        }

        var filter = new StructuredValueFilter(column, value, exclude);
        if (!Filters.Contains(filter))
        {
            Filters.Add(filter);
        }
    }

    [RelayCommand]
    private void RemoveFilter(StructuredValueFilter? filter)
    {
        if (filter is not null)
        {
            Filters.Remove(filter);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Clear();
        SearchText = null;
    }

    /// <summary>Selects the row's line in the document, or opens the whole-file browser there once it has left the buffer.</summary>
    [RelayCommand]
    private void JumpToLine(StructuredGridRow? row)
    {
        if (row is null || _document.TryNavigateToLineNumber(row.LineNumber))
        {
            return;
        }

        StatusMessage = _document.RequestFileBrowser(new FileBrowserTarget(row.LineNumber, null))
            ? Loc.Get("Vm_Browser_OpenedAtLine")
            : Loc.Get("Vm_Search_LineEvicted");
    }

    private void ApplyView()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var filters = Filters.ToList();
        var visible = _allRows
            .Where(r => filters.All(f => f.Matches(r.Event)))
            .Where(r => search is null || MatchesSearch(r, search))
            .ToList();
        visible.Sort(CompareRows);

        var selected = SelectedRow?.LineNumber;
        Rows = visible;
        SelectedRow = selected is { } line ? visible.FirstOrDefault(r => r.LineNumber == line) : null;
        StatusMessage = _allRows.Count == 0
            ? Loc.Get("Vm_Grid_NoEvents")
            : Loc.Format("Vm_Grid_Summary", visible.Count, _allRows.Count);
    }

    private static bool MatchesSearch(StructuredGridRow row, string search) =>
        row.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || row.Level?.Contains(search, StringComparison.OrdinalIgnoreCase) == true
        || row.Values.Any(v => v?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>The sort column's order (missing values last either way), then line order.</summary>
    private int CompareRows(StructuredGridRow a, StructuredGridRow b)
    {
        int result;
        switch (SortColumn)
        {
            case LineColumn:
                result = a.LineNumber.CompareTo(b.LineNumber);
                break;
            case TimeColumn when a.Timestamp is null || b.Timestamp is null:
                result = a.Timestamp is null ? (b.Timestamp is null ? 0 : 1) : -1;
                return result != 0 ? result : a.LineNumber.CompareTo(b.LineNumber);
            case TimeColumn:
                result = a.Timestamp!.Value.CompareTo(b.Timestamp!.Value);
                break;
            default:
                var x = CellValue(a, SortColumn);
                var y = CellValue(b, SortColumn);
                if (x is null || y is null)
                {
                    result = StructuredColumns.CompareValues(x, y);
                    return result != 0 ? result : a.LineNumber.CompareTo(b.LineNumber);
                }

                result = StructuredColumns.CompareValues(x, y);
                break;
        }

        if (SortDescending)
        {
            result = -result;
        }

        return result != 0 ? result : a.LineNumber.CompareTo(b.LineNumber);
    }

    public void Dispose()
    {
        _document.Lines.CollectionChanged -= OnDocumentLinesChanged;
        _liveRefreshTimer.Stop();
        _refreshCts?.Cancel();
    }
}
