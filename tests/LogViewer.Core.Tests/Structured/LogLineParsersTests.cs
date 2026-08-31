using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class LogLineParsersTests
{
    [Fact]
    public void Detect_SerilogClef_WinsOverGenericJson()
    {
        string[] lines =
        [
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""a {X}"",""X"":1}",
            @"{""@t"":""2026-01-01T00:00:01Z"",""@mt"":""b {X}"",""X"":2}",
            @"{""@t"":""2026-01-01T00:00:02Z"",""@mt"":""c {X}"",""X"":3}",
        ];

        Assert.Equal("serilog", LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Detect_GenericJson_WhenNotSerilog()
    {
        string[] lines =
        [
            @"{""level"":""info"",""msg"":""one""}",
            @"{""level"":""warn"",""msg"":""two""}",
            @"{""level"":""error"",""msg"":""three""}",
        ];

        Assert.Equal("ndjson", LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Detect_Logfmt()
    {
        string[] lines =
        [
            "level=info msg=one component=a",
            "level=warn msg=two component=b",
            "level=error msg=three component=c",
        ];

        Assert.Equal("logfmt", LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Detect_Syslog()
    {
        string[] lines =
        [
            "<34>1 2026-01-02T03:04:05Z h a - - - one",
            "<35>1 2026-01-02T03:04:06Z h a - - - two",
            "<36>1 2026-01-02T03:04:07Z h a - - - three",
        ];

        Assert.Equal("syslog", LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Detect_W3c_IgnoresDirectiveLinesInScoring()
    {
        string[] lines =
        [
            "#Software: Microsoft Internet Information Services 10.0",
            "#Version: 1.0",
            "#Fields: date time cs-method cs-uri-stem sc-status",
            "2026-01-02 03:04:05 GET /a 200",
            "2026-01-02 03:04:06 GET /b 200",
            "2026-01-02 03:04:07 GET /c 404",
        ];

        Assert.Equal("w3c", LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Detect_PlainText_ReturnsNull()
    {
        string[] lines =
        [
            "2026-01-02 03:04:05 INFO  Starting up",
            "2026-01-02 03:04:06 INFO  Ready",
            "2026-01-02 03:04:07 ERROR Something broke",
        ];

        Assert.Null(LogLineParsers.Detect(lines));
    }

    [Fact]
    public void Create_RoundTripsEveryAdvertisedFormatId()
    {
        foreach (var id in LogLineParsers.FormatIds)
        {
            var parser = LogLineParsers.Create(id);
            Assert.NotNull(parser);
            Assert.Equal(id, parser!.FormatId);
        }

        Assert.Null(LogLineParsers.Create("nope"));
    }

    [Fact]
    public void DetectFile_ReadsFromStart()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(path,
            [
                "level=info msg=a k=1",
                "level=info msg=b k=2",
                "level=warn msg=c k=3",
                "level=error msg=d k=4",
            ]);

            Assert.Equal("logfmt", LogLineParsers.DetectFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
