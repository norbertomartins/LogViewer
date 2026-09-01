using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Search;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal full-file/EventLog search window. Runs independently of the live tail — a
/// match's line number may no longer be in the document's bounded ring buffer, in which case
/// double-clicking a result just can't scroll to it (the result row itself still shows the full text).
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private readonly TailDocumentViewModel _document;
    private readonly IFullTextSearchService _fileSearchService;
    private readonly IEventLogSearchService _eventLogSearchService;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _pattern = string.Empty;

    [ObservableProperty]
    private string _propertyName = string.Empty;

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _isCaseSensitive;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private SearchResult? _selectedResult;

    public SearchViewModel(TailDocumentViewModel document, IFullTextSearchService fileSearchService, IEventLogSearchService eventLogSearchService)
    {
        _document = document;
        _fileSearchService = fileSearchService;
        _eventLogSearchService = eventLogSearchService;
    }

    public ObservableCollection<SearchResult> Results { get; } = [];

    public string TargetDescription => _document.SearchableEventLog is { } eventLog
        ? $"EventLog channel: {eventLog.Channel}"
        : _document.SearchableFilePath ?? "(nothing to search)";

    /// <summary>Whether to show the "Property" field — only meaningful for a file search over a document
    /// currently in structured (Serilog JSON) view.</summary>
    public bool CanSearchByProperty => _document.SearchableEventLog is null && _document.IsStructuredView;

    public static IReadOnlyList<string> WellKnownProperties => StructuredFieldResolver.WellKnownFields;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrEmpty(Pattern))
        {
            StatusMessage = Loc.Get("Vm_Search_EnterPattern");
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        Results.Clear();
        IsSearching = true;
        StatusMessage = Loc.Get("Vm_Search_Searching");

        var matchCount = 0;
        try
        {
            var stream = BuildStream(cts.Token);
            if (stream is null)
            {
                StatusMessage = Loc.Get("Vm_Search_NothingToSearch");
                return;
            }

            await foreach (var result in stream)
            {
                Results.Add(result);
                matchCount++;
            }

            StatusMessage = matchCount == 1
                ? Loc.Get("Vm_Search_MatchesOne")
                : Loc.Format("Vm_Search_MatchesMany", matchCount);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = matchCount == 1
                ? Loc.Get("Vm_Search_CancelledOne")
                : Loc.Format("Vm_Search_CancelledMany", matchCount);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexParseException)
        {
            StatusMessage = Loc.Format("Vm_Search_Failed", ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    private IAsyncEnumerable<SearchResult>? BuildStream(CancellationToken cancellationToken)
    {
        if (_document.SearchableEventLog is { } eventLog)
        {
            return _eventLogSearchService.SearchAsync(eventLog.Channel, eventLog.Filters, Pattern, IsRegex, IsCaseSensitive, cancellationToken);
        }

        var property = CanSearchByProperty && !string.IsNullOrWhiteSpace(PropertyName) ? PropertyName : null;
        return _document.SearchableFilePath is { } path
            ? _fileSearchService.SearchAsync(path, Pattern, IsRegex, IsCaseSensitive, property, cancellationToken)
            : null;
    }

    [RelayCommand]
    private void CancelSearch() => _searchCts?.Cancel();

    [RelayCommand]
    private void JumpToSelected()
    {
        if (SelectedResult is null)
        {
            return;
        }

        if (!_document.TryNavigateToLineNumber(SelectedResult.LineNumber))
        {
            StatusMessage = Loc.Get("Vm_Search_LineEvicted");
        }
    }
}
