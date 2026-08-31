using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class MergedTimestampExtractorTests
{
    [Theory]
    [InlineData("2026-01-02T03:04:05.123Z rest", "2026-01-02T03:04:05.123Z")]
    [InlineData("2026-01-02 03:04:05,123 INFO msg", "2026-01-02T03:04:05.123Z")]
    [InlineData("2026-01-02 03:04:05 INFO msg", "2026-01-02T03:04:05Z")]
    [InlineData("[2026-01-02T03:04:05+02:00] hi", "2026-01-02T03:04:05+02:00")]
    public void TryExtract_RecognizesCommonShapes(string line, string expectedIso)
    {
        var result = MergedTimestampExtractor.TryExtract(line);
        Assert.NotNull(result);
        Assert.Equal(DateTimeOffset.Parse(expectedIso), result!.Value.ToUniversalTime());
    }

    [Fact]
    public void TryExtract_TimeOnly_AnchorsToFixedDate_SoLinesStillSort()
    {
        var a = MergedTimestampExtractor.TryExtract("12:00:01 INFO a");
        var b = MergedTimestampExtractor.TryExtract("12:00:02 INFO b");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.True(a < b);
    }

    [Theory]
    [InlineData("plain line no timestamp")]
    [InlineData("   at Foo.Bar() in File.cs:line 42")]
    [InlineData("value=1234567890")]
    public void TryExtract_NoTimestamp_ReturnsNull(string line) =>
        Assert.Null(MergedTimestampExtractor.TryExtract(line));
}
