namespace LogViewer.Core.Analysis;

/// <summary>Aggregates a whole structured log file into frequency tables — either by "shape" (grouping
/// occurrences of the same log statement regardless of its dynamic values, via
/// <see cref="BlockDiff.MessageSignature"/>) or by a structured property's value (e.g. which
/// <c>SourceContext</c>/module produced the most Error-level lines). Neither existed before this: the
/// engine had per-block grouping (<see cref="BlockDiff.IBlockScanService"/>) but nothing that counted
/// occurrences of "the same kind of line" across an entire file.</summary>
public interface IPatternFrequencyAnalyzer
{
    /// <summary>Groups by <see cref="BlockDiff.MessageSignature.Compute"/>, optionally filtered to
    /// <paramref name="minLevel"/> and above (via <see cref="Structured.LogLevelSeverity.Rank"/>),
    /// returning up to <paramref name="topN"/> entries ordered by descending count.</summary>
    Task<IReadOnlyList<PatternFrequencyEntry>> AnalyzeBySignatureAsync(
        string sourcePath, string? minLevel, int topN, CancellationToken cancellationToken);

    /// <summary>Groups by the resolved value of <paramref name="propertyName"/> (via
    /// <see cref="Structured.StructuredFieldResolver.Resolve"/>), optionally filtered to
    /// <paramref name="minLevel"/> and above. When <paramref name="useExceptionFrameFallback"/> is true,
    /// an event whose property resolves to null/empty but that carries an exception falls back to
    /// <see cref="ExceptionFrameExtractor.ExtractTopFrame"/> — this is what lets "top error sources" work
    /// even on logs that never set <c>SourceContext</c>. Returns up to <paramref name="topN"/> entries
    /// ordered by descending count.</summary>
    Task<IReadOnlyList<PropertyFrequencyEntry>> AnalyzeByPropertyAsync(
        string sourcePath, string propertyName, string? minLevel, bool useExceptionFrameFallback, int topN, CancellationToken cancellationToken);
}
