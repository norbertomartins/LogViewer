namespace LogViewer.Core.BlockDiff;

public enum BlockDetectionStrategy
{
    ByCorrelationField,
    ByProximity,
}

/// <summary>Tuning knobs for segmenting a scanned file into <see cref="LogBlock"/>s.</summary>
/// <param name="Strategy">Which grouping strategy to use.</param>
/// <param name="CorrelationField">Required when <see cref="Strategy"/> is <see cref="BlockDetectionStrategy.ByCorrelationField"/>.</param>
/// <param name="ProximityMaxGap">Max timestamp gap between consecutive lines to keep clustering them together (proximity strategy).</param>
/// <param name="ProximityMaxLines">Max lines a single proximity cluster can grow to before it's force-finalized.</param>
/// <param name="MaxTrackedGroups">Memory bound on concurrently-open correlation groups (correlation strategy) — the
/// least-recently-updated group is evicted (finalized early) once this cap is exceeded, so pathologically wide files
/// with huge numbers of distinct correlation ids stay bounded at the cost of only approximate results in that case.</param>
/// <param name="QuietLineGap">Lines of no activity before a correlation group is considered finished and finalized/yielded.</param>
public sealed record BlockDetectionOptions(
    BlockDetectionStrategy Strategy,
    string? CorrelationField,
    TimeSpan ProximityMaxGap,
    int ProximityMaxLines,
    int MaxTrackedGroups = 5000,
    int QuietLineGap = 5000)
{
    public static BlockDetectionOptions ByCorrelation(string correlationField) =>
        new(BlockDetectionStrategy.ByCorrelationField, correlationField, TimeSpan.FromSeconds(2), 200);

    public static BlockDetectionOptions ByProximity(TimeSpan? maxGap = null, int maxLines = 200) =>
        new(BlockDetectionStrategy.ByProximity, null, maxGap ?? TimeSpan.FromSeconds(2), maxLines);
}

/// <summary>Streams an entire target file and segments its structured lines into <see cref="LogBlock"/>s, one per
/// logical operation, without materializing the whole file in memory.</summary>
public interface IBlockScanService
{
    IAsyncEnumerable<LogBlock> ScanAsync(string targetPath, BlockDetectionOptions options, CancellationToken cancellationToken);
}
