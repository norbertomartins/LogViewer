namespace LogViewer.Core.Analysis;

/// <summary>One line's contribution to the volume timeline: when it happened, how severe it was, and
/// which display line it is (so a clicked bar can scroll to it).</summary>
public readonly record struct VolumeSample(DateTimeOffset Timestamp, int Severity, long LineNumber);

/// <summary>A single time bucket of the volume timeline.</summary>
public sealed record VolumeBin(
    DateTimeOffset Start,
    TimeSpan Width,
    int Total,
    int Warnings,
    int Errors,
    long FirstLineNumber,
    long LastLineNumber)
{
    public DateTimeOffset End => Start + Width;

    /// <summary>Lines that are neither warnings nor errors.</summary>
    public int Info => Math.Max(0, Total - Warnings - Errors);
}

/// <summary>
/// Buckets timestamped log lines into a fixed-width histogram for the volume timeline. Pure and
/// UI-free: the app feeds it the visible lines' timestamps/levels and renders the returned bins.
/// </summary>
public static class LogVolumeBinner
{
    /// <summary>"Nice" bucket widths, smallest to largest, used by <see cref="ChooseBucket"/>.</summary>
    private static readonly TimeSpan[] NiceBuckets =
    [
        TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1), TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
    ];

    /// <summary>Severity rank at/above which a sample counts as an error (matches <c>LogLevelSeverity.Rank("Error")</c>).</summary>
    public const int ErrorSeverity = 4;

    /// <summary>Severity rank that counts as a warning.</summary>
    public const int WarningSeverity = 3;

    /// <summary>Smallest "nice" bucket width that keeps <paramref name="span"/> under <paramref name="targetBins"/> buckets.</summary>
    public static TimeSpan ChooseBucket(TimeSpan span, int targetBins = 120)
    {
        if (span <= TimeSpan.Zero || targetBins < 1)
        {
            return NiceBuckets[0];
        }

        foreach (var candidate in NiceBuckets)
        {
            if (span / candidate <= targetBins)
            {
                return candidate;
            }
        }

        return NiceBuckets[^1];
    }

    /// <summary>
    /// Bins <paramref name="samples"/> (any order) into consecutive fixed-width buckets. When
    /// <paramref name="bucket"/> is null a width is chosen automatically from the sample time span.
    /// Empty gaps between populated buckets are included so the timeline keeps a true time axis.
    /// Returns an empty list when there are fewer than two distinct timestamps.
    /// </summary>
    public static IReadOnlyList<VolumeBin> Bin(IEnumerable<VolumeSample> samples, TimeSpan? bucket = null, int maxBins = 500)
    {
        var ordered = samples.OrderBy(s => s.Timestamp).ToList();
        if (ordered.Count == 0)
        {
            return [];
        }

        var min = ordered[0].Timestamp;
        var max = ordered[^1].Timestamp;
        if (max <= min)
        {
            return [];
        }

        var width = bucket ?? ChooseBucket(max - min);
        if (width <= TimeSpan.Zero)
        {
            return [];
        }

        // Cap the bin count so a huge span with a tiny explicit bucket can't allocate unbounded.
        var estimated = (max - min).Ticks / width.Ticks + 1;
        if (estimated > maxBins)
        {
            width = TimeSpan.FromTicks((long)Math.Ceiling((double)(max - min).Ticks / maxBins));
        }

        var origin = min;
        var bins = new List<VolumeBin>();
        var index = 0;

        while (index < ordered.Count)
        {
            var binStart = origin + TimeSpan.FromTicks(width.Ticks * bins.Count);
            var binEnd = binStart + width;

            int total = 0, warnings = 0, errors = 0;
            long first = -1, last = -1;

            while (index < ordered.Count && ordered[index].Timestamp < binEnd)
            {
                var s = ordered[index];
                total++;
                if (s.Severity >= ErrorSeverity)
                {
                    errors++;
                }
                else if (s.Severity == WarningSeverity)
                {
                    warnings++;
                }

                if (first < 0)
                {
                    first = s.LineNumber;
                }

                last = s.LineNumber;
                index++;
            }

            bins.Add(new VolumeBin(binStart, width, total, warnings, errors, first, last));

            if (bins.Count > maxBins)
            {
                break;
            }
        }

        return bins;
    }
}
