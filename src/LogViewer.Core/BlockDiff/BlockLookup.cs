namespace LogViewer.Core.BlockDiff;

/// <summary>Finds the <see cref="LogBlock"/> that contains a specific line number, by streaming
/// <see cref="IBlockScanService.ScanAsync"/> and returning the first matching block — lets a caller that
/// only has a bare line number (e.g. an MCP tool call) resolve the same block object the UI builds when
/// a user picks a line to run block-diff/similarity against.</summary>
public static class BlockLookup
{
    public static async Task<LogBlock?> FindBlockContainingLineAsync(
        IBlockScanService scanService, string sourcePath, BlockDetectionOptions options, long lineNumber, CancellationToken cancellationToken)
    {
        await foreach (var block in scanService.ScanAsync(sourcePath, options, cancellationToken).ConfigureAwait(false))
        {
            if (block.Lines.Any(l => l.LineNumber == lineNumber))
            {
                return block;
            }
        }

        return null;
    }
}
