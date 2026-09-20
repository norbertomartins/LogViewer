using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class SerilogTraceTreeBuilderTests
{
    private static StructuredLogEvent Span(
        string traceId, string spanId, string? parentSpanId, string name,
        DateTimeOffset start, TimeSpan duration, string level = "Information") =>
        new(
            Timestamp: start + duration,
            Level: level,
            MessageTemplate: name,
            RenderedMessage: name,
            Exception: null,
            Properties: parentSpanId is null
                ? new Dictionary<string, string> { ["SpanStartTimestamp"] = start.ToString("O") }
                : new Dictionary<string, string> { ["SpanStartTimestamp"] = start.ToString("O"), ["ParentSpanId"] = parentSpanId },
            TraceId: traceId,
            SpanId: spanId);

    [Fact]
    public void BuildTree_NestsChildrenUnderParentByParentSpanId()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var lines = new List<(long, StructuredLogEvent)>
        {
            (1, Span("trace1", "root", null, "GET /orders", t0, TimeSpan.FromMilliseconds(300))),
            (2, Span("trace1", "child1", "root", "SELECT orders", t0.AddMilliseconds(10), TimeSpan.FromMilliseconds(100))),
            (3, Span("trace1", "child2", "root", "SELECT items", t0.AddMilliseconds(120), TimeSpan.FromMilliseconds(150))),
        };

        var tree = SerilogTraceTreeBuilder.BuildTree(lines, "trace1");

        var root = Assert.Single(tree);
        Assert.Equal("root", root.SpanId);
        Assert.Equal("GET /orders", root.Name);
        Assert.Equal(TimeSpan.FromMilliseconds(300), root.Duration);
        Assert.Equal(2, root.Children.Count);
        Assert.Equal("child1", root.Children[0].SpanId);
        Assert.Equal("child2", root.Children[1].SpanId);
    }

    [Fact]
    public void BuildTree_OrphanParent_BecomesItsOwnRoot()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var lines = new List<(long, StructuredLogEvent)>
        {
            // Parent span fell outside the buffered window — child still surfaces as a root, not dropped.
            (1, Span("trace1", "child1", "missing-parent", "SELECT orders", t0, TimeSpan.FromMilliseconds(50))),
        };

        var tree = SerilogTraceTreeBuilder.BuildTree(lines, "trace1");

        var root = Assert.Single(tree);
        Assert.Equal("child1", root.SpanId);
        Assert.Empty(root.Children);
    }

    [Fact]
    public void BuildTree_IgnoresOtherTracesAndNonSpanLines()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var plainLog = new StructuredLogEvent(t0, "Information", "hello", "hello", null,
            new Dictionary<string, string> { ["TraceId"] = "trace1", ["SpanId"] = "span-x" }, "trace1", "span-x");
        var lines = new List<(long, StructuredLogEvent)>
        {
            (1, Span("trace1", "root", null, "GET /orders", t0, TimeSpan.FromMilliseconds(100))),
            (2, Span("trace2", "other-root", null, "GET /unrelated", t0, TimeSpan.FromMilliseconds(50))),
            (3, plainLog),
        };

        var tree = SerilogTraceTreeBuilder.BuildTree(lines, "trace1");

        var root = Assert.Single(tree);
        Assert.Equal("root", root.SpanId);
    }

    [Fact]
    public void SummarizeTraces_GroupsByTraceId_NewestFirst()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var lines = new List<(long, StructuredLogEvent)>
        {
            (1, Span("trace1", "root1", null, "op1", t0, TimeSpan.FromMilliseconds(10))),
            (2, Span("trace1", "child1", "root1", "op1a", t0, TimeSpan.FromMilliseconds(5))),
            (3, Span("trace2", "root2", null, "op2", t0.AddSeconds(5), TimeSpan.FromMilliseconds(10))),
        };

        var summaries = SerilogTraceTreeBuilder.SummarizeTraces(lines);

        Assert.Equal(2, summaries.Count);
        Assert.Equal("trace2", summaries[0].TraceId);
        Assert.Equal(1, summaries[0].SpanCount);
        Assert.Equal("trace1", summaries[1].TraceId);
        Assert.Equal(2, summaries[1].SpanCount);
    }
}
