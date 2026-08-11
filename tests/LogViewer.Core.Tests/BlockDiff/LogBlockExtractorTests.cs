using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class LogBlockExtractorTests
{
    private static StructuredLogEvent Evt(string message, DateTimeOffset? ts, params (string Key, string Value)[] props) =>
        new(ts, "Information", null, message, null, props.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void ExtractByCorrelation_KeepsOnlyMatchingLines_InOrder()
    {
        var events = new List<(long LineNumber, StructuredLogEvent Event)>
        {
            (1, Evt("start", null, ("TraceId", "abc"))),
            (2, Evt("other op", null, ("TraceId", "xyz"))),
            (3, Evt("middle", null, ("TraceId", "abc"))),
            (4, Evt("unrelated", null)),
            (5, Evt("end", null, ("TraceId", "abc"))),
        };

        var block = LogBlockExtractor.ExtractByCorrelation(events, "TraceId", "abc", "doc");

        Assert.Equal([1L, 3L, 5L], block.Lines.Select(l => l.LineNumber));
        Assert.Equal("TraceId", block.CorrelationField);
        Assert.Equal("abc", block.CorrelationValue);
    }

    [Fact]
    public void ExtractByProximity_StopsAtTimeGap()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<(long LineNumber, StructuredLogEvent Event)>
        {
            (1, Evt("far before", t0)),
            (2, Evt("just before", t0.AddSeconds(9))),
            (3, Evt("anchor", t0.AddSeconds(10))),
            (4, Evt("just after", t0.AddSeconds(11))),
            (5, Evt("far after", t0.AddSeconds(30))),
        };

        var block = LogBlockExtractor.ExtractByProximity(events, anchorIndex: 2, sourceDescription: "doc", maxGap: TimeSpan.FromSeconds(2));

        Assert.Equal([2L, 3L, 4L], block.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void ExtractByProximity_StopsAtThreadChange_WhenAnchorHasThreadId()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var events = new List<(long LineNumber, StructuredLogEvent Event)>
        {
            (1, Evt("other thread", t0, ("ThreadId", "9"))),
            (2, Evt("anchor", t0.AddSeconds(1), ("ThreadId", "1"))),
            (3, Evt("same thread", t0.AddSeconds(2), ("ThreadId", "1"))),
        };

        var block = LogBlockExtractor.ExtractByProximity(events, anchorIndex: 1, sourceDescription: "doc", maxGap: TimeSpan.FromSeconds(5));

        Assert.Equal([2L, 3L], block.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public void ExtractByProximity_NoTimestamps_FallsBackToLineCountWindow()
    {
        var events = new List<(long LineNumber, StructuredLogEvent Event)>
        {
            (1, Evt("a", null)),
            (2, Evt("b", null)),
            (3, Evt("anchor", null)),
            (4, Evt("c", null)),
        };

        var block = LogBlockExtractor.ExtractByProximity(events, anchorIndex: 2, sourceDescription: "doc", maxGap: TimeSpan.FromSeconds(2));

        Assert.Equal([1L, 2L, 3L, 4L], block.Lines.Select(l => l.LineNumber));
    }
}
