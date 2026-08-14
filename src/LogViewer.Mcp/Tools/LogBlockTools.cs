using System.ComponentModel;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

[McpServerToolType]
public sealed class LogBlockTools(IBlockScanService blockScanService, ISimilarBlockFinder similarBlockFinder)
{
    private static readonly int ErrorRank = LogLevelSeverity.Rank("Error")!.Value;

    [McpServerTool(Name = "logs_scan_blocks")]
    [Description(
        "Segments a structured log file into logical operation blocks, either by a shared correlation-id " +
        "property (e.g. TraceId/CorrelationId) or by line/time/thread proximity when no correlation id exists. " +
        "Each block reports whether it contains an Error-or-above line.")]
    public async Task<IReadOnlyList<BlockSummary>> ScanBlocks(
        [Description("Full path to the structured (Serilog JSON) log file.")] string sourcePath,
        [Description("\"correlation\" or \"proximity\".")] string strategy,
        [Description("Required when strategy is \"correlation\": the structured property to group by (e.g. TraceId).")]
        string? correlationField,
        [Description("Max timestamp gap in seconds between consecutive lines to keep clustering them together (proximity strategy).")]
        double? proximityMaxGapSeconds,
        [Description("Max lines a single proximity cluster can grow to (proximity strategy).")] int? proximityMaxLines,
        [Description("Maximum number of blocks to return.")] int maxBlocks,
        CancellationToken cancellationToken)
    {
        var options = BuildOptions(strategy, correlationField, proximityMaxGapSeconds, proximityMaxLines);
        var cap = ResponseLimits.ClampRows(maxBlocks);

        var blocks = new List<BlockSummary>(cap);
        await foreach (var block in blockScanService.ScanAsync(sourcePath, options, cancellationToken).ConfigureAwait(false))
        {
            if (blocks.Count >= cap)
            {
                break;
            }

            blocks.Add(ToSummary(block));
        }

        return blocks;
    }

    [McpServerTool(Name = "logs_find_similar_blocks")]
    [Description(
        "Finds blocks in a target file most similar to the operation block containing a given anchor line — " +
        "useful for comparing a failing run against successful ones.")]
    public async Task<IReadOnlyList<ScoredBlockSummary>> FindSimilarBlocks(
        [Description("Full path to the file containing the anchor line.")] string anchorSourcePath,
        [Description("Line number (1-based) inside the anchor block.")] long anchorLineNumber,
        [Description("Full path to the file to search for similar blocks.")] string targetPath,
        [Description("\"correlation\" or \"proximity\".")] string strategy,
        [Description("Required when strategy is \"correlation\": the structured property to group by.")] string? correlationField,
        [Description("Max timestamp gap in seconds between consecutive lines (proximity strategy).")] double? proximityMaxGapSeconds,
        [Description("Max lines a single proximity cluster can grow to (proximity strategy).")] int? proximityMaxLines,
        [Description("Maximum number of ranked matches to return.")] int topN,
        CancellationToken cancellationToken)
    {
        var options = BuildOptions(strategy, correlationField, proximityMaxGapSeconds, proximityMaxLines);
        var cap = Math.Clamp(topN <= 0 ? 10 : topN, 1, 50);

        var anchor = await BlockLookup.FindBlockContainingLineAsync(blockScanService, anchorSourcePath, options, anchorLineNumber, cancellationToken)
            .ConfigureAwait(false);
        if (anchor is null)
        {
            return [];
        }

        var matches = await similarBlockFinder.FindBestMatchesAsync(anchor, targetPath, options, cap, cancellationToken).ConfigureAwait(false);
        return matches.Select(m => new ScoredBlockSummary(m.Score, ToSummary(m.Block))).ToList();
    }

    private static BlockDetectionOptions BuildOptions(string strategy, string? correlationField, double? proximityMaxGapSeconds, int? proximityMaxLines)
    {
        if (string.Equals(strategy, "correlation", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(correlationField))
            {
                throw new ArgumentException("correlationField is required when strategy is \"correlation\".", nameof(correlationField));
            }

            return BlockDetectionOptions.ByCorrelation(correlationField);
        }

        var maxGap = proximityMaxGapSeconds is > 0 ? TimeSpan.FromSeconds(proximityMaxGapSeconds.Value) : (TimeSpan?)null;
        var maxLines = proximityMaxLines is > 0 ? proximityMaxLines.Value : 200;
        return BlockDetectionOptions.ByProximity(maxGap, maxLines);
    }

    private static BlockSummary ToSummary(LogBlock block)
    {
        var hasErrorOrAbove = block.Lines.Any(l => (LogLevelSeverity.Rank(l.Event.Level) ?? -1) >= ErrorRank);
        var sampleLines = block.Lines.Take(5)
            .Select(l => new SearchResultDto(l.LineNumber, ResponseLimits.Truncate(l.Event.RenderedMessage)))
            .ToList();

        return new BlockSummary(
            block.CorrelationField,
            block.CorrelationValue,
            block.SourceDescription,
            block.Lines.Count == 0 ? 0 : block.Lines[0].LineNumber,
            block.Lines.Count == 0 ? 0 : block.Lines[^1].LineNumber,
            block.Lines.Count,
            hasErrorOrAbove,
            sampleLines);
    }
}
