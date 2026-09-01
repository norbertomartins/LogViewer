using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal "Find Similar Block" comparison window: locates the block of structured log
/// lines belonging to the same operation as an anchor line, in another open document or a file browsed
/// from disk, and shows a side-by-side diff. Modeled on <see cref="SearchViewModel"/> — a non-modal
/// auxiliary window operating on a document, independent of the live tail.
/// </summary>
public sealed partial class SimilarBlockViewModel : ObservableObject
{
    private const int TopCandidateCount = 5;

    private readonly TailDocumentViewModel _sourceDocument;
    private readonly LogLineViewModel _anchorLine;
    private readonly ISimilarBlockFinder _blockFinder;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _findCts;
    private LogBlock? _anchorBlock;

    public SimilarBlockViewModel(
        TailDocumentViewModel sourceDocument,
        LogLineViewModel anchorLine,
        IReadOnlyList<TailDocumentViewModel> openDocuments,
        ISimilarBlockFinder blockFinder,
        IDialogService dialogService)
    {
        _sourceDocument = sourceDocument;
        _anchorLine = anchorLine;
        _blockFinder = blockFinder;
        _dialogService = dialogService;

        OpenTargetDocuments = openDocuments.Where(d => !ReferenceEquals(d, sourceDocument)).ToList();
        SuggestedCorrelationFields = CorrelationKeySelector.SuggestFields(anchorLine.Structured!);
        _selectedCorrelationField = SuggestedCorrelationFields.Count > 0 ? SuggestedCorrelationFields[0] : null;
    }

    public string SourceDescription => $"{_sourceDocument.Title} — line {_anchorLine.LineNumber}";

    public IReadOnlyList<TailDocumentViewModel> OpenTargetDocuments { get; }

    /// <summary>Correlation-field suggestions for the editable combo — auto-suggested (best guess first)
    /// but always user-confirmable/overridable, never silently applied.</summary>
    public IReadOnlyList<string> SuggestedCorrelationFields { get; }

    [ObservableProperty]
    private string? _selectedCorrelationField;

    [ObservableProperty]
    private TailDocumentViewModel? _selectedTargetDocument;

    [ObservableProperty]
    private string? _browsedTargetPath;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ScoredBlock? _selectedCandidate;

    public ObservableCollection<ScoredBlock> Candidates { get; } = [];

    public ObservableCollection<DiffEntry> DiffEntries { get; } = [];

    [ObservableProperty]
    private DiffEntry? _selectedDiffEntry;

    partial void OnSelectedTargetDocumentChanged(TailDocumentViewModel? value)
    {
        if (value is not null)
        {
            BrowsedTargetPath = null;
        }
    }

    partial void OnSelectedCandidateChanged(ScoredBlock? value)
    {
        DiffEntries.Clear();
        if (value is null || _anchorBlock is null)
        {
            return;
        }

        foreach (var entry in BlockAlignment.Align(_anchorBlock, value.Block))
        {
            DiffEntries.Add(entry);
        }
    }

    [RelayCommand]
    private void BrowseTarget()
    {
        var paths = _dialogService.ShowOpenFileDialog();
        if (paths is not { Count: > 0 })
        {
            return;
        }

        BrowsedTargetPath = paths[0];
        SelectedTargetDocument = null;
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        var targetPath = SelectedTargetDocument?.SearchableFilePath ?? BrowsedTargetPath;
        if (string.IsNullOrEmpty(targetPath))
        {
            StatusMessage = Loc.Get("Vm_Similar_ChooseTarget");
            return;
        }

        var anchor = BuildAnchorBlock();
        if (anchor is null || anchor.Lines.Count == 0)
        {
            StatusMessage = Loc.Get("Vm_Similar_NoBlock");
            return;
        }

        _anchorBlock = anchor;

        _findCts?.Cancel();
        var cts = new CancellationTokenSource();
        _findCts = cts;

        Candidates.Clear();
        DiffEntries.Clear();
        SelectedCandidate = null;
        IsSearching = true;
        StatusMessage = Loc.Get("Vm_Similar_Searching");

        try
        {
            var options = string.IsNullOrEmpty(SelectedCorrelationField)
                ? BlockDetectionOptions.ByProximity()
                : BlockDetectionOptions.ByCorrelation(SelectedCorrelationField);

            var matches = await _blockFinder.FindBestMatchesAsync(anchor, targetPath, options, TopCandidateCount, cts.Token);

            foreach (var match in matches)
            {
                Candidates.Add(match);
            }

            StatusMessage = matches.Count switch
            {
                0 => Loc.Get("Vm_Similar_NoMatch"),
                1 => Loc.Get("Vm_Similar_CandidatesOne"),
                _ => Loc.Format("Vm_Similar_CandidatesMany", matches.Count),
            };

            SelectedCandidate = Candidates.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.Get("Vm_Similar_Cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.Format("Vm_Similar_ScanFailed", ex.Message);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private void CancelFind() => _findCts?.Cancel();

    [RelayCommand]
    private void JumpToLeft(DiffEntry? entry)
    {
        if (entry?.Left is { } left)
        {
            _sourceDocument.TryNavigateToLineNumber(left.LineNumber);
        }
    }

    [RelayCommand]
    private void JumpToRight(DiffEntry? entry)
    {
        if (entry?.Right is not { } right)
        {
            return;
        }

        if (SelectedTargetDocument is not null)
        {
            if (!SelectedTargetDocument.TryNavigateToLineNumber(right.LineNumber))
            {
                StatusMessage = Loc.Get("Vm_Similar_LineGone");
            }
        }
        else
        {
            StatusMessage = Loc.Get("Vm_Similar_TargetNotOpen");
        }
    }

    /// <summary>Builds the anchor block around <see cref="_anchorLine"/>: by the confirmed correlation field
    /// when one is set, or by line/time proximity as a fallback.</summary>
    private LogBlock? BuildAnchorBlock()
    {
        var events = _sourceDocument.StructuredLines;

        if (!string.IsNullOrEmpty(SelectedCorrelationField))
        {
            var value = StructuredFieldResolver.Resolve(_anchorLine.Structured, SelectedCorrelationField);
            if (string.IsNullOrEmpty(value))
            {
                StatusMessage = Loc.Format("Vm_Similar_NoProperty", SelectedCorrelationField);
                return null;
            }

            return LogBlockExtractor.ExtractByCorrelation(events, SelectedCorrelationField, value, _sourceDocument.Title);
        }

        var anchorIndex = -1;
        for (var i = 0; i < events.Count; i++)
        {
            if (events[i].LineNumber == _anchorLine.LineNumber)
            {
                anchorIndex = i;
                break;
            }
        }

        return anchorIndex < 0
            ? null
            : LogBlockExtractor.ExtractByProximity(events, anchorIndex, _sourceDocument.Title, TimeSpan.FromSeconds(2), maxLinesEachDirection: 200);
    }
}
