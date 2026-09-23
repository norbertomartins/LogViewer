using LogViewer.Core.Analysis;
using LogViewer.Core.Structured;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Analysis;

public sealed class CorrelationIdExtractorTests
{
    [Fact]
    public void Extracts_KeyValuePairs_WithKnownIdKeys()
    {
        var ids = CorrelationIdExtractor.Extract("2026-09-23 INFO handled request_id=req-8812 trace_id: abc123def user=bob");

        Assert.Contains(new CorrelationId("request_id", "req-8812"), ids);
        Assert.Contains(new CorrelationId("trace_id", "abc123def"), ids);
        Assert.DoesNotContain(ids, i => i.Value == "bob");
    }

    [Fact]
    public void Extracts_JsonKeys_Traceparent_AndGuids()
    {
        var ids = CorrelationIdExtractor.Extract(
            """{"CorrelationId":"c-42x","x-request-id":"r-9","tp":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01","sid":"8b3c1f0e-2a4d-4c55-9e8f-0123456789ab"}""");

        Assert.Contains(new CorrelationId("CorrelationId", "c-42x"), ids);
        Assert.Contains(new CorrelationId("x-request-id", "r-9"), ids);
        Assert.Contains(new CorrelationId("TraceId", "0af7651916cd43dd8448eb211c80319c"), ids);
        Assert.Contains(new CorrelationId("SpanId", "b7ad6b7169203331"), ids);
        Assert.Contains(new CorrelationId("GUID", "8b3c1f0e-2a4d-4c55-9e8f-0123456789ab"), ids);
    }

    [Fact]
    public void Extracts_StructuredTraceFields_AndIdProperties_Deduplicated()
    {
        var evt = new StructuredLogEvent(null, "Information", null, "msg", null,
            new Dictionary<string, string> { ["RequestId"] = "0HM123", ["UserName"] = "alice" },
            TraceId: "t-1234", SpanId: "s-5678");

        var ids = CorrelationIdExtractor.Extract("""{"TraceId":"t-1234","RequestId":"0HM123"}""", evt);

        Assert.Equal(["t-1234", "s-5678", "0HM123"], ids.Select(i => i.Value));
    }

    [Fact]
    public void IgnoresTimestampsAndPlainWords() =>
        Assert.Empty(CorrelationIdExtractor.Extract("2026-09-23T10:11:12Z level=info msg=hello"));
}

public sealed class ExceptionGrouperTests
{
    private static IEnumerable<ExceptionScanLine> Lines(params string[] texts) =>
        texts.Select((t, i) => new ExceptionScanLine(i + 1, t));

    [Fact]
    public void GroupsIdenticalDotNetTraces_IgnoringLineNumbersAndMessages()
    {
        var groups = ExceptionGrouper.Group(Lines(
            "10:00:01 ERR request failed",
            "System.InvalidOperationException: Order 17 not found",
            "   at Shop.Orders.Load(Int32 id) in C:\\src\\Orders.cs:line 42",
            "   at Shop.Api.Get() in C:\\src\\Api.cs:line 10",
            "10:00:02 INF ok",
            "System.InvalidOperationException: Order 99 not found",
            "   at Shop.Orders.Load(Int32 id) in D:\\build\\Orders.cs:line 44",
            "   at Shop.Api.Get() in D:\\build\\Api.cs:line 11",
            "System.TimeoutException: gave up",
            "   at Shop.Db.Query()"));

        Assert.Equal(2, groups.Count);
        var first = groups[0];
        Assert.Equal("System.InvalidOperationException", first.ExceptionType);
        Assert.Equal(2, first.Count);
        Assert.Equal(2, first.FirstLineNumber);
        Assert.Equal(6, first.LastLineNumber);
        Assert.Equal([2L, 6], first.LineNumbers);
        Assert.StartsWith("Shop.Orders.Load", first.TopFrame);
        Assert.Equal("Order 17 not found", first.SampleMessage);
        Assert.Contains("Api.cs:line 10", first.SampleText);
        Assert.Equal("System.TimeoutException", groups[1].ExceptionType);
    }

    [Fact]
    public void IgnoresExceptionNamesMentionedWithoutAStackTrace()
    {
        var groups = ExceptionGrouper.Group(Lines(
            "retrying after TimeoutException: will try again",
            "next line"));

        Assert.Empty(groups);
    }

    [Fact]
    public void HandlesJavaCausedByAndMoreLines()
    {
        var groups = ExceptionGrouper.Group(Lines(
            "java.lang.IllegalStateException: boom",
            "\tat com.acme.Service.run(Service.java:120)",
            "\tat com.acme.Main.main(Main.java:5)",
            "Caused by: java.io.IOException: disk",
            "\tat com.acme.Disk.write(Disk.java:9)",
            "\t... 2 more",
            "after"));

        var group = Assert.Single(groups);
        Assert.Equal("java.lang.IllegalStateException", group.ExceptionType);
        Assert.Contains("Caused by", group.SampleText);
    }

    [Fact]
    public void HandlesPythonTracebacks_TopFrameIsTheInnermostCall()
    {
        var groups = ExceptionGrouper.Group(Lines(
            "Traceback (most recent call last):",
            "  File \"app.py\", line 10, in main",
            "    run()",
            "  File \"worker.py\", line 3, in run",
            "    raise ValueError(\"bad\")",
            "ValueError: bad value 42",
            "done"));

        var group = Assert.Single(groups);
        Assert.Equal("ValueError", group.ExceptionType);
        Assert.Equal("bad value 42", group.SampleMessage);
        Assert.Equal("worker.py in run", group.TopFrame);
        Assert.Equal(1, group.FirstLineNumber);
    }

    [Fact]
    public void GroupsStructuredExceptions_AndTracksTimestamps()
    {
        var t1 = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);
        const string Ex = "System.NullReferenceException: Object reference not set\n   at A.B.C()\n   at A.B.D()";
        StructuredLogEvent Evt(DateTimeOffset ts) => new(ts, "Error", null, "failed", Ex, new Dictionary<string, string>());

        var groups = ExceptionGrouper.Group(
        [
            new ExceptionScanLine(5, "{json}", Evt(t1), t1),
            new ExceptionScanLine(9, "{json}", Evt(t1.AddMinutes(5)), t1.AddMinutes(5)),
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal(t1, group.FirstSeen);
        Assert.Equal(t1.AddMinutes(5), group.LastSeen);
        Assert.Equal("A.B.C()", group.TopFrame);
    }

    [Fact]
    public async Task GroupFileAsync_StreamsAFile()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(
            "2026-09-23 10:00:00 ERROR x\nSystem.IO.IOException: disk full\n   at Io.Write()\n" +
            "2026-09-23 10:05:00 ERROR y\nSystem.IO.IOException: disk full again\n   at Io.Write()\n");

        var groups = await ExceptionGrouper.GroupFileAsync(file.FilePath);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Count);
        Assert.Equal([2L, 5], group.LineNumbers);
    }
}
