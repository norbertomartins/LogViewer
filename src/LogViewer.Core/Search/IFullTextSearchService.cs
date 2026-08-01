namespace LogViewer.Core.Search;

public sealed record SearchResult(long LineNumber, long ByteOffset, string Text);

/// <summary>
/// Searches an entire tail source (file or, later, EventLog) rather than just its buffered/visible
/// portion. Implemented in Phase 3 as a streaming scan that reports progress and yields results
/// incrementally instead of materializing the whole file.
/// </summary>
public interface IFullTextSearchService
{
    IAsyncEnumerable<SearchResult> SearchAsync(string sourcePath, string pattern, bool isRegex, bool isCaseSensitive, CancellationToken cancellationToken);
}
