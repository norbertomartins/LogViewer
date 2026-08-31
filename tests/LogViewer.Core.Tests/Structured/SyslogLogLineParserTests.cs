using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class SyslogLogLineParserTests
{
    private readonly SyslogLogLineParser _parser = new();

    [Fact]
    public void TryParse_Rfc5424_WithStructuredData_Parses()
    {
        var line = "<165>1 2026-01-02T03:04:05.000Z host1 myapp 8710 ID47 [exampleSDID@32473 iut=\"3\" eventID=\"1011\"] Application restarted";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), evt!.Timestamp);
        Assert.Equal("Information", evt.Level); // severity 165 % 8 = 5 → Notice/Info
        Assert.Equal("Application restarted", evt.RenderedMessage);
        Assert.Equal("host1", evt.Properties["host"]);
        Assert.Equal("myapp", evt.Properties["appname"]);
        Assert.Equal("3", evt.Properties["iut"]);
        Assert.Equal("1011", evt.Properties["eventID"]);
    }

    [Fact]
    public void TryParse_Rfc5424_NilStructuredData_And_ErrorSeverity()
    {
        var line = "<11>1 2026-01-02T03:04:05Z host1 app - - - disk failure";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("Error", evt!.Level); // 11 % 8 = 3 → Error
        Assert.Equal("disk failure", evt.RenderedMessage);
    }

    [Fact]
    public void TryParse_Rfc3164_BsdShape_Parses()
    {
        var line = "<34>Oct 11 22:14:15 mymachine su: 'su root' failed for user on /dev/pts/8";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("Fatal", evt!.Level); // 34 % 8 = 2 → Critical
        Assert.Equal("mymachine", evt.Properties["host"]);
        Assert.Equal("su", evt.Properties["tag"]);
        Assert.Contains("su root", evt.RenderedMessage);
    }

    [Theory]
    [InlineData("plain log line")]
    [InlineData("2026-01-02 12:00:00 INFO something")]
    public void TryParse_NonSyslog_ReturnsFalse(string line)
    {
        Assert.False(_parser.TryParse(line, out var evt));
        Assert.Null(evt);
    }
}
