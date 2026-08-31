using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class LogVolumeBinnerTests
{
    private static VolumeSample S(int secondsFromEpoch, int severity, long line) =>
        new(DateTimeOffset.UnixEpoch.AddSeconds(secondsFromEpoch), severity, line);

    [Fact]
    public void Bin_GroupsBySecond_AndCountsSeverities()
    {
        VolumeSample[] samples =
        [
            S(0, 2, 1), S(0, 4, 2), S(0, 3, 3),   // bucket 0: 3 total, 1 error, 1 warning
            S(1, 2, 4),                             // bucket 1: 1 total
            S(3, 5, 5), S(3, 2, 6),               // bucket 3: 2 total, 1 error (Fatal >= ErrorSeverity)
        ];

        var bins = LogVolumeBinner.Bin(samples, TimeSpan.FromSeconds(1));

        Assert.Equal(4, bins.Count); // buckets 0..3, gap at 2 included
        Assert.Equal((3, 1, 1), (bins[0].Total, bins[0].Errors, bins[0].Warnings));
        Assert.Equal(1, bins[0].FirstLineNumber);
        Assert.Equal(3, bins[0].LastLineNumber);
        Assert.Equal(1, bins[1].Total);
        Assert.Equal(0, bins[2].Total); // gap bucket
        Assert.Equal(-1, bins[2].FirstLineNumber);
        Assert.Equal((2, 1, 0), (bins[3].Total, bins[3].Errors, bins[3].Warnings));
    }

    [Fact]
    public void Bin_FewerThanTwoDistinctTimestamps_ReturnsEmpty()
    {
        Assert.Empty(LogVolumeBinner.Bin([]));
        Assert.Empty(LogVolumeBinner.Bin([S(5, 2, 1), S(5, 2, 2)]));
    }

    [Fact]
    public void Bin_AutoBucket_KeepsBinCountBounded()
    {
        var samples = Enumerable.Range(0, 400).Select(i => S(i * 60, 2, i));
        var bins = LogVolumeBinner.Bin(samples);

        Assert.True(bins.Count is > 0 and <= 500);
    }

    [Fact]
    public void ChooseBucket_PicksNiceWidthUnderTarget()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), LogVolumeBinner.ChooseBucket(TimeSpan.FromSeconds(90), targetBins: 120));
        Assert.Equal(TimeSpan.FromSeconds(30), LogVolumeBinner.ChooseBucket(TimeSpan.FromHours(1), targetBins: 120));
        Assert.Equal(TimeSpan.FromHours(1), LogVolumeBinner.ChooseBucket(TimeSpan.FromDays(2), targetBins: 60));
    }

    [Fact]
    public void Bin_OrdersUnsortedInput()
    {
        VolumeSample[] samples = [S(10, 2, 3), S(0, 2, 1), S(5, 2, 2)];

        var bins = LogVolumeBinner.Bin(samples, TimeSpan.FromSeconds(5));

        Assert.Equal(1, bins[0].FirstLineNumber);
        Assert.Equal(3, bins[^1].LastLineNumber);
    }
}
