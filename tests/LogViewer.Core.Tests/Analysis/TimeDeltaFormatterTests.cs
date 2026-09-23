using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class TimeDeltaFormatterTests
{
    [Theory]
    [InlineData(0, "+0ms")]
    [InlineData(12, "+12ms")]
    [InlineData(1234, "+1.234s")]
    [InlineData(-1234, "-1.234s")]
    [InlineData(123_000, "+2m03s")]
    [InlineData(3_720_000, "+1h02m")]
    [InlineData(183_600_000, "+2d03h")]
    public void Format_IsCompactAndSigned(long ms, string expected) =>
        Assert.Equal(expected, TimeDeltaFormatter.Format(TimeSpan.FromMilliseconds(ms)));
}
