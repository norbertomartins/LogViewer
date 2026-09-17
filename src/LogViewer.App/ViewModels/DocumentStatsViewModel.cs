using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.Core.Analysis;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>
/// Backs the non-modal per-document statistics panel: live counts/rate from the tailed document itself,
/// plus an on-demand full-file scan for the top recurring message patterns via the same
/// <see cref="IPatternFrequencyAnalyzer"/> the MCP <c>logs_top_patterns</c> tool uses. Modeled on
/// <see cref="SimilarBlockViewModel"/> — a non-modal auxiliary window operating on a document,
/// independent of the live tail.
/// </summary>
public sealed partial class DocumentStatsViewModel : ObservableObject, IDisposable
{
    private const int TopPatternCount = 20;

    private readonly TailDocumentViewModel _document;
    private readonly IPatternFrequencyAnalyzer _patternAnalyzer;
    private readonly DispatcherTimer _rateTimer;
    private CancellationTokenSource? _analyzeCts;
    private long _lastLineCount;
    private DateTime _lastSampleAt;

    public DocumentStatsViewModel(TailDocumentViewModel document, IPatternFrequencyAnalyzer patternAnalyzer)
    {
        _document = document;
        _patternAnalyzer = patternAnalyzer;

        _lastLineCount = _document.TotalLinesAppended;
        _lastSampleAt = DateTime.UtcNow;

        _rateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _rateTimer.Tick += (_, _) => SampleRate();
        _rateTimer.Start();

        RecomputeCounts();
        _ = AnalyzeAsync();
    }

    public string Title => _document.Title;

    [ObservableProperty]
    private double _linesPerSecond;

    [ObservableProperty]
    private bool _isAnalyzing;

    [ObservableProperty]
    private string? _statusMessage;

    public ObservableCollection<PatternFrequencyEntry> TopPatterns { get; } = [];

    public long TotalLines => _document.TotalLinesAppended;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    /// <summary>Recomputes error/warning counts over the currently buffered (displayed) lines — the same
    /// severity resolution the row-filter in <see cref="Views.Documents.TailDocumentView"/> uses: the
    /// structured level when the line parsed, else a best-effort guess from the raw text.</summary>
    private void RecomputeCounts()
    {
        var errors = 0;
        var warnings = 0;
        foreach (var line in _document.Lines)
        {
            var rank = LogLevelSeverity.Rank(line.Structured?.Level) ?? LogLevelNormalizer.GuessSeverityFromLine(line.Text);
            if (rank is null)
            {
                continue;
            }

            if (rank >= LogLevelSeverity.Rank("Error"))
            {
                errors++;
            }
            else if (rank >= LogLevelSeverity.Rank("Warning"))
            {
                warnings++;
            }
        }

        ErrorCount = errors;
        WarningCount = warnings;
    }

    private void SampleRate()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastSampleAt).TotalSeconds;
        if (elapsed <= 0)
        {
            return;
        }

        var current = _document.TotalLinesAppended;
        LinesPerSecond = Math.Max(0, current - _lastLineCount) / elapsed;
        _lastLineCount = current;
        _lastSampleAt = now;
        OnPropertyChanged(nameof(TotalLines));
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        var path = _document.SearchableFilePath;
        if (string.IsNullOrEmpty(path))
        {
            StatusMessage = Loc.Get("Vm_Stats_NoFile");
            return;
        }

        _analyzeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _analyzeCts = cts;

        RecomputeCounts();
        IsAnalyzing = true;
        StatusMessage = Loc.Get("Vm_Stats_Analyzing");
        TopPatterns.Clear();

        try
        {
            var entries = await _patternAnalyzer.AnalyzeBySignatureAsync(path, minLevel: null, TopPatternCount, cts.Token);
            foreach (var entry in entries)
            {
                TopPatterns.Add(entry);
            }

            StatusMessage = entries.Count == 0 ? Loc.Get("Vm_Stats_NoPatterns") : null;
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
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private void JumpToPattern(PatternFrequencyEntry? entry)
    {
        if (entry is not null)
        {
            _document.TryNavigateToLineNumber(entry.FirstLineNumber);
        }
    }

    public void Dispose()
    {
        _analyzeCts?.Cancel();
        _rateTimer.Stop();
    }
}
