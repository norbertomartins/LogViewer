using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class NewPatternDetectorTests
{
    private const int Info = 2;
    private const int Error = LogVolumeBinner.ErrorSeverity;

    [Fact]
    public void FlagsOnlyUnseenShapes_AtOrAboveTheMinimumSeverity_AfterWarmup()
    {
        var detector = new NewPatternDetector(warmupLines: 3);

        Assert.False(detector.Observe("Payment 1 failed", Error)); // warm-up
        Assert.False(detector.Observe("started", Info));
        Assert.False(detector.Observe("ok 5", Info));

        Assert.False(detector.Observe("Payment 99 failed", Error)); // same shape as a warm-up line
        Assert.True(detector.Observe("Disk full: 91% used", Error));
        Assert.False(detector.Observe("Disk full: 97% used", Error)); // number masked -> same shape
        Assert.False(detector.Observe("brand new info message", Info)); // below min severity
        Assert.True(detector.Observe("ok 7", Error)); // known text at a new severity is a new shape
    }

    [Fact]
    public void Reset_ForgetsTheBaseline()
    {
        var detector = new NewPatternDetector(warmupLines: 0);
        Assert.True(detector.Observe("boom", Error));
        Assert.False(detector.Observe("boom", Error));

        detector.Reset();
        Assert.True(detector.Observe("boom", Error));
    }
}

public sealed class VolumeSpikeDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    private static VolumeBin Bin(int i, int total, int errors = 0) =>
        new(T0.AddMinutes(i), TimeSpan.FromMinutes(1), total, 0, errors, i, i);

    [Fact]
    public void FlagsVolumeAndErrorSpikes_AgainstTheRecentBaseline()
    {
        var bins = Enumerable.Range(0, 20).Select(i => Bin(i, 20 + (i % 3), errors: i % 2)).ToList();
        bins[12] = Bin(12, 200);
        bins[15] = Bin(15, 21, errors: 12);

        var result = VolumeSpikeDetector.Detect(bins);

        Assert.True(result[12].IsVolumeSpike);
        Assert.False(result[12].IsErrorSpike);
        Assert.True(result[15].IsErrorSpike);
        Assert.False(result[15].IsVolumeSpike);
        Assert.Equal(2, result.Count(b => b.IsSpike));
    }

    [Fact]
    public void NeverFlagsBinsWithoutEnoughHistory_OrBelowTheAbsoluteMinimum()
    {
        var bins = new List<VolumeBin> { Bin(0, 1), Bin(1, 1), Bin(2, 500) };
        Assert.DoesNotContain(VolumeSpikeDetector.Detect(bins), b => b.IsSpike);

        var quiet = Enumerable.Range(0, 10).Select(i => Bin(i, 1)).Append(Bin(10, 8)).ToList();
        Assert.DoesNotContain(VolumeSpikeDetector.Detect(quiet), b => b.IsSpike); // 8 < minTotal
    }
}
