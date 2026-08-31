using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class LogfmtLogLineParserTests
{
    private readonly LogfmtLogLineParser _parser = new();

    [Fact]
    public void TryParse_WellKnownKeys_MapOntoEvent()
    {
        var line = @"ts=2026-01-02T03:04:05Z level=warn msg=""disk almost full"" component=storage free=12";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), evt!.Timestamp);
        Assert.Equal("Warning", evt.Level);
        Assert.Equal("disk almost full", evt.RenderedMessage);
        Assert.Equal("storage", evt.Properties["component"]);
        Assert.Equal("12", evt.Properties["free"]);
    }

    [Fact]
    public void TryParse_QuotedValueWithEscapes_IsUnescaped()
    {
        var line = @"level=info msg=""line one\nline two"" key=val";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("line one\nline two", evt!.RenderedMessage);
    }

    [Fact]
    public void TryParse_ErrorKey_BecomesException()
    {
        var line = @"level=error msg=""request failed"" error=""System.TimeoutException: timed out""";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("Error", evt!.Level);
        Assert.Equal("System.TimeoutException: timed out", evt.Exception);
    }

    [Fact]
    public void TryParse_UnixSecondsTimestamp_Parsed()
    {
        Assert.True(_parser.TryParse("level=info msg=hi ts=1704164645", out var evt));
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), evt!.Timestamp);
    }

    [Theory]
    [InlineData("just a plain sentence with no pairs")]
    [InlineData(@"{""json"":true}")]
    [InlineData("single=pair")]
    [InlineData("a=1 b=2")] // pairs but no msg/level
    public void TryParse_NonLogfmt_ReturnsFalse(string line)
    {
        Assert.False(_parser.TryParse(line, out var evt));
        Assert.Null(evt);
    }
}
