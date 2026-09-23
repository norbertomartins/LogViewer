using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.Core.Analysis;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal "Exceptions" panel: every exception/stack trace in a document grouped by type + top frames
/// (<see cref="ExceptionGrouper"/>), over either the lines currently in the ring buffer (instant) or the whole
/// file (streamed on a background thread — the same analysis the MCP <c>logs_exception_groups</c> tool runs).
/// Occurrences jump into the live document, or into the whole-file browser when they've left the ring buffer.
/// </summary>
public sealed partial class ExceptionGroupsViewModel : ObservableObject, IDisposable
{
    private readonly TailDocumentViewModel _document;
    private CancellationTokenSource? _analyzeCts;

    public ExceptionGroupsViewModel(TailDocumentViewModel document)
    {
        _document = document;
        _ = AnalyzeAsync();
    }

    public string Title => _document.Title;

    public bool CanScanWholeFile => _document.SearchableFilePath is not null;

    /// <summary>False = the lines currently buffered in the document; true = the entire file on disk.</summary>
    [ObservableProperty]
    private bool _isWholeFile;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private ExceptionGroup? _selectedGroup;

    public ObservableCollection<ExceptionGroup> Groups { get; } = [];

    partial void OnIsWholeFileChanged(bool value) => _ = AnalyzeAsync();

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        _analyzeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analyzeCts = cts;

        IsAnalyzing = true;
        StatusMessage = Loc.Get("Vm_Exceptions_Analyzing");
        Groups.Clear();

        try
        {
            IReadOnlyList<ExceptionGroup> groups;
            if (IsWholeFile && _document.SearchableFilePath is { } path)
            {
                groups = await ExceptionGrouper.GroupFileAsync(path, cts.Token);
            }
            else
            {
                // Snapshot on the UI thread; the regex-heavy scan runs in the background.
                var snapshot = _document.Lines.Select(l => (l.LineNumber, l.Text, l.Structured)).ToList();
                var formatId = _document.StructuredFormatId;
                groups = await Task.Run(() => GroupBuffer(snapshot, formatId, cts.Token), cts.Token);
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var group in groups)
            {
                Groups.Add(group);
            }

            SelectedGroup = Groups.FirstOrDefault();
            StatusMessage = groups.Count == 0
                ? Loc.Get("Vm_Exceptions_None")
                : Loc.Format("Vm_Exceptions_Summary", groups.Count, groups.Sum(g => g.Count));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.Format("Vm_Stats_AnalyzeFailed", ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_analyzeCts, cts))
            {
                IsAnalyzing = false;
            }
        }
    }

    /// <summary>Lines that weren't parsed for display (structured view off) are still tried with the document's
    /// format parser, so an exception embedded in a JSON line is found either way.</summary>
    private static IReadOnlyList<ExceptionGroup> GroupBuffer(
        IReadOnlyList<(long LineNumber, string Text, StructuredLogEvent? Structured)> lines, string formatId, CancellationToken token)
    {
        var parser = LogLineParsers.Create(formatId);
        var grouper = new ExceptionGrouper();
        foreach (var (lineNumber, text, structured) in lines)
        {
            token.ThrowIfCancellationRequested();
            if (lineNumber <= 0)
            {
                continue; // reset/switch marker rows
            }

            var raw = MergedTailSource.StripLabel(text);
            var evt = structured ?? (parser is not null && parser.TryParse(raw, out var parsed) ? parsed : null);
            grouper.Add(new ExceptionScanLine(lineNumber, raw, evt, evt?.Timestamp ?? MergedTimestampExtractor.TryExtract(raw)));
        }

        return grouper.Complete();
    }

    /// <summary>Selects <paramref name="lineNumber"/> in the live document, or opens the whole-file browser there
    /// when the line has already left the ring buffer.</summary>
    [RelayCommand]
    private void JumpToLine(long lineNumber)
    {
        if (_document.TryNavigateToLineNumber(lineNumber))
        {
            StatusMessage = null;
            return;
        }

        StatusMessage = _document.RequestFileBrowser(new FileBrowserTarget(lineNumber, null))
            ? Loc.Get("Vm_Browser_OpenedAtLine")
            : Loc.Get("Vm_Search_LineEvicted");
    }

    [RelayCommand]
    private void BookmarkOccurrences()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        foreach (var lineNumber in SelectedGroup.LineNumbers)
        {
            _document.EnsureBookmarked(lineNumber);
        }

        StatusMessage = Loc.Format("Vm_Exceptions_Bookmarked", SelectedGroup.LineNumbers.Count);
    }

    [RelayCommand]
    private void CopySample()
    {
        if (SelectedGroup is null)
        {
            return;
        }

        try
        {
            System.Windows.Clipboard.SetText(SelectedGroup.SampleText);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    public void Dispose() => _analyzeCts?.Cancel();
}
