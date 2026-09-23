using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class RegexLogLineParserTests
{
    private static RegexLogLineParser Create(string pattern, string? timestampFormat = null, bool utc = true)
    {
        var format = new CustomLogFormat { Name = "My app", Pattern = pattern, TimestampFormat = timestampFormat, TimestampIsUtc = utc };
        Assert.True(RegexLogLineParser.TryCreate(format, out var parser, out var error), error);
        return parser!;
    }

    [Fact]
    public void MapsWellKnownGroups_AndTurnsTheRestIntoProperties()
    {
        var parser = Create(@"^(?<timestamp>\S+ \S+) \[(?<thread>\d+)\] (?<level>\w+) (?<logger>\S+) - (?<message>.*)$");

        Assert.True(parser.TryParse("2026-09-23 14:05:00,123 [12] WARN Shop.Orders - stock low for sku 42", out var evt));

        Assert.Equal(new DateTimeOffset(2026, 9, 23, 14, 5, 0, 123, TimeSpan.Zero), evt!.Timestamp);
        Assert.Equal("Warning", evt.Level);
        Assert.Equal("stock low for sku 42", evt.RenderedMessage);
        Assert.Equal("12", evt.Properties["thread"]);
        Assert.Equal("Shop.Orders", evt.Properties["logger"]);
        Assert.Equal("My app", parser.DisplayName);
    }

    [Fact]
    public void CombinesSeparateDateAndTimeGroups_WithAnExactFormat()
    {
        var parser = Create(@"^(?<date>\d{2}/\d{2}/\d{4}) (?<time>[\d:.]+) (?<msg>.*)$", "dd/MM/yyyy HH:mm:ss.fff");

        Assert.True(parser.TryParse("23/09/2026 08:00:01.500 hello", out var evt));
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 8, 0, 1, 500, TimeSpan.Zero), evt!.Timestamp);
        Assert.Equal("hello", evt.RenderedMessage);
        Assert.Null(evt.Level);
    }

    [Fact]
    public void ReadsUnixEpochTimestamps()
    {
        var parser = Create(@"^(?<ts>\d+(?:\.\d+)?) (?<message>.*)$");

        Assert.True(parser.TryParse("1790000000.25 boot", out var evt));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_250), evt!.Timestamp);
    }

    [Fact]
    public void NonMatchingLine_ReturnsFalse_AndMissingMessageFallsBackToTheLine()
    {
        var parser = Create(@"^(?<level>INFO|ERROR) ");

        Assert.False(parser.TryParse("   at Some.Frame()", out _));
        Assert.True(parser.TryParse("ERROR boom", out var evt));
        Assert.Equal("ERROR boom", evt!.RenderedMessage);
        Assert.Equal("Error", evt.Level);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(unclosed")]
    [InlineData(@"^\d+ .*$")]
    public void TryCreate_RejectsEmptyInvalidOrUnnamedPatterns(string pattern)
    {
        Assert.False(RegexLogLineParser.TryCreate(new CustomLogFormat { Pattern = pattern }, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Registry_OffersCustomFormatsFirst_AndDetectsThem()
    {
        var custom = new CustomLogFormat { Name = "Registry test", Pattern = @"^@@RXTEST@@ (?<level>\w+) (?<message>.*)$" };
        var broken = new CustomLogFormat { Name = "Broken", Pattern = "(" };
        try
        {
            LogLineParsers.SetCustomFormats([custom, broken]);

            Assert.Equal(custom.Id, LogLineParsers.FormatIds[0]);
            Assert.DoesNotContain(broken.Id, LogLineParsers.FormatIds);
            Assert.Equal("Registry test", LogLineParsers.DisplayNameFor(custom.Id));
            Assert.IsType<RegexLogLineParser>(LogLineParsers.Create(custom.Id));
            Assert.Equal(custom.Id, LogLineParsers.Detect(["@@RXTEST@@ INFO a", "@@RXTEST@@ ERROR b", "@@RXTEST@@ INFO c"]));
        }
        finally
        {
            LogLineParsers.SetCustomFormats([]);
        }

        Assert.Null(LogLineParsers.Create(custom.Id));
    }
}

public sealed class CustomLogFormatExamplesTests
{
    [Theory]
    [InlineData("log4j", "2026-09-23 14:05:00,123 [main] ERROR com.acme.App - boom", "Error", "boom")]
    [InlineData("python", "2026-09-23 14:05:00,123 - app.worker - WARNING - slow", "Warning", "slow")]
    [InlineData("generic", "2026-09-23T14:05:00.5Z [INFO] started", "Information", "started")]
    public void Examples_ParseTheirOwnShape(string exampleSuffix, string line, string level, string message)
    {
        var format = CustomLogFormatExamples.All.Single(f => f.Id.EndsWith(exampleSuffix, StringComparison.Ordinal));
        Assert.True(RegexLogLineParser.TryCreate(format, out var parser, out var error), error);

        Assert.True(parser!.TryParse(line, out var evt));
        Assert.Equal(level, evt!.Level);
        Assert.Equal(message, evt.RenderedMessage);
        Assert.NotNull(evt.Timestamp);
    }

    [Fact]
    public void AccessLogExample_ParsesNcsaTimestampAndFields()
    {
        var format = CustomLogFormatExamples.All.Single(f => f.Id.EndsWith("access", StringComparison.Ordinal));
        Assert.True(RegexLogLineParser.TryCreate(format, out var parser, out _));

        Assert.True(parser!.TryParse(
            "10.0.0.7 - - [23/Sep/2026:14:05:00 +0100] \"GET /api/orders?id=7 HTTP/1.1\" 503 512 \"-\" \"curl/8.0\"", out var evt));

        Assert.Equal(new DateTimeOffset(2026, 9, 23, 14, 5, 0, TimeSpan.FromHours(1)), evt!.Timestamp);
        Assert.Equal("503", evt.Properties["status"]);
        Assert.Equal("/api/orders?id=7", evt.Properties["path"]);
    }
}
