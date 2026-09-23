using LogViewer.Core.BlockDiff;

namespace LogViewer.Core.Analysis;

/// <summary>
/// Flags the first occurrence of a message "shape" never seen before in a document — typically a brand-new kind
/// of error appearing in a long-running service. A line's shape is its severity plus the message with dynamic
/// tokens masked (<see cref="MessageSignature.Mask"/>: numbers, ids, GUIDs, IPs, timestamps, quoted strings), so
/// "Payment 123 failed" and "Payment 456 failed" are the same shape.
/// <para>Nothing is flagged during a warm-up of <c>warmupLines</c> lines (the baseline), and only lines at or above
/// <c>minSeverity</c> are considered. Because severity is part of the shape, lines below it can never make a
/// flaggable shape "seen", so they only count towards the warm-up and skip the (regex-heavy) masking entirely —
/// cheap enough for the live tail path. An Information message later logged at Error still counts as new.
/// Memory is bounded: past <see cref="MaxShapes"/> distinct shapes the detector stops learning (and flagging)
/// rather than growing without limit on unstructured noise.</para>
/// </summary>
public sealed class NewPatternDetector(int warmupLines = 200, int minSeverity = LogVolumeBinner.WarningSeverity)
{
    public const int MaxShapes = 50_000;

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private long _observed;

    /// <summary>Records one line; returns true when it is a new shape worth flagging.</summary>
    public bool Observe(string message, int? severity)
    {
        _observed++;
        if (severity is not { } s || s < minSeverity || _seen.Count >= MaxShapes)
        {
            return false;
        }

        var isNew = _seen.Add($"{s}|{MessageSignature.Mask(message)}");
        return isNew && _observed > warmupLines;
    }

    public void Reset()
    {
        _seen.Clear();
        _observed = 0;
    }
}

/// <summary>
/// Marks timeline bins whose volume (or error count) jumps well above the recent baseline: a bin is a spike when
/// it exceeds the mean of the preceding <c>window</c> bins by more than <c>sigmas</c> standard deviations (with a
/// floor on the deviation so a perfectly flat baseline doesn't make every small wobble a spike) and reaches an
/// absolute minimum count. Bins with too little history are never flagged.
/// </summary>
public static class VolumeSpikeDetector
{
    public static IReadOnlyList<VolumeBin> Detect(
        IReadOnlyList<VolumeBin> bins, double sigmas = 3, int window = 30, int minHistory = 5, int minTotal = 10, int minErrors = 3)
    {
        var result = new VolumeBin[bins.Count];
        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            var from = Math.Max(0, i - window);
            var history = i - from;
            if (history < minHistory)
            {
                result[i] = bin;
                continue;
            }

            var (meanTotal, sdTotal) = Stats(bins, from, i, b => b.Total);
            var (meanErrors, sdErrors) = Stats(bins, from, i, b => b.Errors);

            var volumeSpike = bin.Total >= minTotal && bin.Total > meanTotal + (sigmas * Math.Max(sdTotal, 1));
            var errorSpike = bin.Errors >= minErrors && bin.Errors > meanErrors + (sigmas * Math.Max(sdErrors, 0.5));
            result[i] = volumeSpike || errorSpike ? bin with { IsVolumeSpike = volumeSpike, IsErrorSpike = errorSpike } : bin;
        }

        return result;
    }

    private static (double Mean, double StdDev) Stats(IReadOnlyList<VolumeBin> bins, int from, int to, Func<VolumeBin, int> value)
    {
        double sum = 0, sumSquares = 0;
        for (var j = from; j < to; j++)
        {
            double v = value(bins[j]);
            sum += v;
            sumSquares += v * v;
        }

        var n = to - from;
        var mean = sum / n;
        return (mean, Math.Sqrt(Math.Max(0, (sumSquares / n) - (mean * mean))));
    }
}
