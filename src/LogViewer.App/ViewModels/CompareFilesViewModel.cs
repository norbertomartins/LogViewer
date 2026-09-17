using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.App.Services;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal "Compare Files" window: a whole-file side-by-side diff, reusing the same
/// <see cref="BlockAlignment"/>/<see cref="DiffEntry"/> engine <see cref="SimilarBlockViewModel"/> uses
/// for an anchored block comparison, but over two entire files with no correlation/anchor step.
/// </summary>
public sealed partial class CompareFilesViewModel : ObservableObject
{
    private readonly IDialogService _dialogService;
    private readonly Action<string, long> _openPath;
    private CancellationTokenSource? _compareCts;

    public CompareFilesViewModel(IDialogService dialogService, Action<string, long> openPath)
    {
        _dialogService = dialogService;
        _openPath = openPath;
    }

    [ObservableProperty]
    private string? _pathA;

    [ObservableProperty]
    private string? _pathB;

    [ObservableProperty]
    private bool _isComparing;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<DiffEntry> DiffEntries { get; } = [];

    [ObservableProperty]
    private DiffEntry? _selectedDiffEntry;

    [RelayCommand]
    private void BrowseA()
    {
        if (_dialogService.ShowOpenFileDialog() is { Count: > 0 } paths)
        {
            PathA = paths[0];
        }
    }

    [RelayCommand]
    private void BrowseB()
    {
        if (_dialogService.ShowOpenFileDialog() is { Count: > 0 } paths)
        {
            PathB = paths[0];
        }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (string.IsNullOrEmpty(PathA) || string.IsNullOrEmpty(PathB))
        {
            StatusMessage = Loc.Get("Vm_Compare_ChooseBoth");
            return;
        }

        _compareCts?.Cancel();
        var cts = new CancellationTokenSource();
        _compareCts = cts;

        DiffEntries.Clear();
        SelectedDiffEntry = null;
        IsComparing = true;
        StatusMessage = Loc.Get("Vm_Compare_Comparing");

        try
        {
            var blockA = await BuildWholeFileBlockAsync(PathA, cts.Token);
            var blockB = await BuildWholeFileBlockAsync(PathB, cts.Token);

            foreach (var entry in BlockAlignment.Align(blockA, blockB))
            {
                DiffEntries.Add(entry);
            }

            var changed = DiffEntries.Count(e => e.Kind != DiffLineKind.Common);
            StatusMessage = Loc.Format("Vm_Compare_Done", DiffEntries.Count, changed);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Loc.Get("Vm_Compare_Cancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = Loc.Format("Vm_Compare_Failed", ex.Message);
        }
        finally
        {
            IsComparing = false;
        }
    }

    private static async Task<LogBlock> BuildWholeFileBlockAsync(string path, CancellationToken cancellationToken)
    {
        var lines = new List<LogBlockLine>();
        await foreach (var (lineNumber, evt) in StructuredFileReader.ReadAsync(path, cancellationToken))
        {
            lines.Add(new LogBlockLine(lineNumber, MessageSignature.Compute(evt), evt));
        }

        return new LogBlock(lines, CorrelationField: null, CorrelationValue: null, SourceDescription: path);
    }

    [RelayCommand]
    private void CancelCompare() => _compareCts?.Cancel();

    [RelayCommand]
    private void JumpToLeft(DiffEntry? entry)
    {
        if (entry?.Left is { } left && !string.IsNullOrEmpty(PathA))
        {
            _openPath(PathA, left.LineNumber);
        }
    }

    [RelayCommand]
    private void JumpToRight(DiffEntry? entry)
    {
        if (entry?.Right is { } right && !string.IsNullOrEmpty(PathB))
        {
            _openPath(PathB, right.LineNumber);
        }
    }
}
