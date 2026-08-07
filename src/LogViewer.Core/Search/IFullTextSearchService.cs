namespace LogViewer.Core.Search;

public sealed record SearchResult(long LineNumber, long ByteOffset, string Text);

/// <summary>
/// Searches an entire tail source (file or, later, EventLog) rather than just its buffered/visible
/// portion. Implemented in Phase 3 as a streaming scan that reports progress and yields results
/// incrementally instead of materializing the whole file.
/// </summary>
public interface IFullTextSearchService
{
    /// <summary>When <paramref name="propertyName"/> is set, only lines that parse as Serilog JSON and whose named
    /// field (see <see cref="Structured.StructuredFieldResolver"/>) matches <paramref name="pattern"/> are returned,
    /// instead of matching anywhere in the raw line.</summary>
    IAsyncEnumerable<SearchResult> SearchAsync(string sourcePath, string pattern, bool isRegex, bool isCaseSensitive, string? propertyName, CancellationToken cancellationToken);
}
