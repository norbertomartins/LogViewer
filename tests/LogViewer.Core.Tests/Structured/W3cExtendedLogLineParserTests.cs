using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class W3cExtendedLogLineParserTests
{
    [Fact]
    public void TryParse_IisRow_AfterFieldsDirective_Parses()
    {
        var parser = new W3cExtendedLogLineParser();

        Assert.False(parser.TryParse("#Software: Microsoft Internet Information Services 10.0", out _));
        Assert.False(parser.TryParse("#Fields: date time s-ip cs-method cs-uri-stem cs-uri-query sc-status time-taken", out _));

        Assert.True(parser.TryParse("2026-01-02 03:04:05 10.0.0.1 GET /home - 500 128", out var evt));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), evt!.Timestamp);
        Assert.Equal("Error", evt.Level); // sc-status 500
        Assert.Equal("GET /home → 500 (128 ms)", evt.RenderedMessage);
        Assert.Equal("GET", evt.Properties["cs-method"]);
        Assert.Equal("500", evt.Properties["sc-status"]);
        Assert.Equal(string.Empty, evt.Properties["cs-uri-query"]);
    }

    [Fact]
    public void TryParse_404_MapsToWarning()
    {
        var parser = new W3cExtendedLogLineParser();
        parser.SetFields(["date", "time", "cs-method", "cs-uri-stem", "sc-status"]);

        Assert.True(parser.TryParse("2026-01-02 03:04:05 GET /missing 404", out var evt));
        Assert.Equal("Warning", evt!.Level);
    }

    [Fact]
    public void TryParse_BeforeAnyFieldsDirective_ReturnsFalse()
    {
        var parser = new W3cExtendedLogLineParser();
        Assert.False(parser.TryParse("2026-01-02 03:04:05 10.0.0.1 GET /home 200 5", out var evt));
        Assert.Null(evt);
    }
}
