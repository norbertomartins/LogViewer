namespace LogViewer.Core.BlockDiff;

public sealed record ScoredBlock(LogBlock Block, double Score);

/// <summary>Orchestrates finding the best-matching block(s) for an anchor block within a target file.</summary>
public interface ISimilarBlockFinder
{
    /// <summary>Streams and scores every block <see cref="IBlockScanService.ScanAsync"/> yields for
    /// <paramref name="targetPath"/> against <paramref name="anchor"/>, returning up to <paramref name="topN"/>
    /// candidates ranked highest score first (so the user can pick among near-ties instead of the top score
    /// being silently assumed correct).</summary>
    Task<IReadOnlyList<ScoredBlock>> FindBestMatchesAsync(
        LogBlock anchor, string targetPath, BlockDetectionOptions options, int topN, CancellationToken cancellationToken);
}

public sealed class SimilarBlockFinder(IBlockScanService scanService) : ISimilarBlockFinder
{
    public async Task<IReadOnlyList<ScoredBlock>> FindBestMatchesAsync(
        LogBlock anchor, string targetPath, BlockDetectionOptions options, int topN, CancellationToken cancellationToken)
    {
        var top = new List<ScoredBlock>(topN);

        await foreach (var candidate in scanService.ScanAsync(targetPath, options, cancellationToken).ConfigureAwait(false))
        {
            var score = BlockSimilarityScorer.Score(anchor, candidate);
            if (score <= 0)
            {
                continue;
            }

            InsertRanked(top, new ScoredBlock(candidate, score), topN);
        }

        return top;
    }

    private static void InsertRanked(List<ScoredBlock> top, ScoredBlock item, int topN)
    {
        var insertAt = top.FindIndex(existing => existing.Score < item.Score);
        if (insertAt < 0)
        {
            if (top.Count < topN)
            {
                top.Add(item);
            }

            return;
        }

        top.Insert(insertAt, item);
        if (top.Count > topN)
        {
            top.RemoveAt(top.Count - 1);
        }
    }
}
