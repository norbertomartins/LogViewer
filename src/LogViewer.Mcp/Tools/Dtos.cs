namespace LogViewer.Mcp.Tools;

public sealed record OpenDocumentSummary(
    string SourcePath, string? SearchableFilePath, string Title, string Kind, bool IsActive, bool IsStructuredView);

public sealed record DescribeSourceResult(
    bool Exists, long? SizeBytes, DateTime? LastWriteUtc, bool LooksStructured, IReadOnlyList<string> SampleFirstLines);

public sealed record SearchResultDto(long LineNumber, string Text);

public sealed record SearchToolResult(IReadOnlyList<SearchResultDto> Results, bool Truncated);

public sealed record LineContextResult(long RequestedLineNumber, bool LineNumberOutOfRange, IReadOnlyList<SearchResultDto> Lines);

public sealed record BlockSummary(
    string? CorrelationField,
    string? CorrelationValue,
    string SourceDescription,
    long FirstLineNumber,
    long LastLineNumber,
    int LineCount,
    bool HasErrorOrAbove,
    IReadOnlyList<SearchResultDto> SampleLines);

public sealed record ScoredBlockSummary(double Score, BlockSummary Block);
