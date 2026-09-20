using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

/// <summary>
/// End-to-end proof that <c>samples/serilog-tracing/orders-api.clef</c> (a fake SerilogTracing-instrumented
/// service log, regenerated via <c>samples/serilog-tracing/generate.py</c>) parses and builds a trace tree
/// exactly as its README describes — the same file a user opens in the running app to see the Trace Tree
/// panel, streamed here through the real <see cref="StructuredFileReader"/> + <see cref="SerilogTraceTreeBuilder"/>
/// pipeline instead of hand-built fixtures.
/// </summary>
public sealed class SerilogTracingSampleTests
{
    private static string SamplePath => Path.Combine(RepoRoot(), "samples", "serilog-tracing", "orders-api.clef");

    private static string WorkerBatchSamplePath =>
        Path.Combine(RepoRoot(), "samples", "serilog-tracing", "worker-batch.clef");

    [Fact]
    public async Task SampleFile_ParsesEveryLine_AsASpanOrACorrelatedLogLine()
    {
        var events = await ReadSampleAsync();

        Assert.Equal(28, events.Count);
        Assert.All(events, e => Assert.NotNull(e.Event.TraceId));

        var traceIds = events.Select(e => e.Event.TraceId).Distinct().ToList();
        Assert.Equal(10, traceIds.Count);

        // The six "Handling request..." lines are correlated to a span (same TraceId/SpanId) but aren't
        // span-completion events themselves — no SpanStartTimestamp, so IsSpan must be false.
        var plainCorrelatedLines = events.Where(e => e.Event.MessageTemplate == "Handling request for order {OrderId}").ToList();
        Assert.Equal(6, plainCorrelatedLines.Count);
        Assert.All(plainCorrelatedLines, e => Assert.False(e.Event.IsSpan));
    }

    [Fact]
    public async Task SampleFile_Order5001Trace_BuildsTwoLevelSpanTree()
    {
        var events = await ReadSampleAsync();
        var order5001Root = events.Single(e => e.Event.IsSpan && e.Event.Properties.GetValueOrDefault("OrderId") == "5001"
            && e.Event.MessageTemplate == "GET /orders/{OrderId}");

        var tree = SerilogTraceTreeBuilder.BuildTree(events, order5001Root.Event.TraceId!);

        var root = Assert.Single(tree);
        Assert.Equal("GET /orders/{OrderId}", root.Name);
        Assert.Equal("Information", root.Level);
        Assert.Equal(2, root.Children.Count);
        Assert.Contains(root.Children, c => c.Name == "SELECT * FROM Orders WHERE Id = {OrderId}");
        Assert.Contains(root.Children, c => c.Name == "GET http://inventory/stock/{Sku}");
        Assert.All(root.Children, c => Assert.NotNull(c.Duration));
    }

    [Fact]
    public async Task SampleFile_Order5004Trace_DbSpanFailed_EscalatesRootToError()
    {
        var events = await ReadSampleAsync();
        var dbSpan = events.Single(e => e.Event.IsSpan && e.Event.Properties.GetValueOrDefault("OrderId") == "5004"
            && e.Event.MessageTemplate == "SELECT * FROM Orders WHERE Id = {OrderId}");

        Assert.Equal("Error", dbSpan.Event.Level);
        Assert.Contains("DbException", dbSpan.Event.Exception);

        var tree = SerilogTraceTreeBuilder.BuildTree(events, dbSpan.Event.TraceId!);
        var root = Assert.Single(tree);
        Assert.Equal("Error", root.Level);
    }

    [Fact]
    public async Task SampleFile_OrphanParentSpan_SurfacesAsItsOwnRoot()
    {
        var events = await ReadSampleAsync();
        var orphan = events.Single(e => e.Event.MessageTemplate == "SELECT * FROM Sessions WHERE Token = {Token}");

        Assert.NotNull(orphan.Event.ParentSpanId);

        var tree = SerilogTraceTreeBuilder.BuildTree(events, orphan.Event.TraceId!);
        var root = Assert.Single(tree);
        Assert.Equal(orphan.Event.SpanId, root.SpanId);
        Assert.Empty(root.Children);
    }

    [Fact]
    public async Task SampleFile_HealthChecks_AreSingleNodeTreesWithNoChildren()
    {
        var events = await ReadSampleAsync();
        var healthSpans = events.Where(e => e.Event.IsSpan && e.Event.MessageTemplate == "GET /health").ToList();

        Assert.Equal(3, healthSpans.Count);
        foreach (var span in healthSpans)
        {
            var tree = SerilogTraceTreeBuilder.BuildTree(events, span.Event.TraceId!);
            var root = Assert.Single(tree);
            Assert.Empty(root.Children);
        }
    }

    [Fact]
    public async Task WorkerBatchSample_Batch1_BuildsThreeLevelSpanTree()
    {
        var events = await ReadSampleAsync(WorkerBatchSamplePath);
        var batch1Root = events.Single(e => e.Event.IsSpan && e.Event.MessageTemplate == "ProcessBatch {BatchId}"
            && e.Event.Properties.GetValueOrDefault("BatchId") == "batch-1");

        var tree = SerilogTraceTreeBuilder.BuildTree(events, batch1Root.Event.TraceId!);

        var root = Assert.Single(tree);
        Assert.Equal("Information", root.Level);
        Assert.Equal(3, root.Children.Count);
        Assert.All(root.Children, item => Assert.Equal("ProcessItem {ItemId}", item.Name));
        Assert.All(root.Children, item => Assert.Single(item.Children));
        Assert.All(root.Children, item => Assert.Equal("SendEmail {ItemId}", item.Children[0].Name));
    }

    [Fact]
    public async Task WorkerBatchSample_Batch2_FailedItem_EscalatesOnlyItsBranchToError()
    {
        var events = await ReadSampleAsync(WorkerBatchSamplePath);
        var batch2Root = events.Single(e => e.Event.IsSpan && e.Event.MessageTemplate == "ProcessBatch {BatchId}"
            && e.Event.Properties.GetValueOrDefault("BatchId") == "batch-2");

        var tree = SerilogTraceTreeBuilder.BuildTree(events, batch2Root.Event.TraceId!);

        var root = Assert.Single(tree);
        Assert.Equal("Error", root.Level);
        Assert.Equal(2, root.Children.Count);

        var failedItem = root.Children.Single(c => c.Level == "Error");
        Assert.Equal("item-5", events.Single(e => e.Event.IsSpan && e.LineNumber == failedItem.LineNumber)
            .Event.Properties["ItemId"]);
        Assert.Contains("InvalidOperationException", events.Single(e => e.LineNumber == failedItem.LineNumber).Event.Exception);

        var healthyItem = root.Children.Single(c => c.Level != "Error");
        Assert.Equal("Information", healthyItem.Level);
    }

    [Fact]
    public async Task WorkerBatchSample_OverlappingTraces_RemainDistinctInSummary()
    {
        var events = await ReadSampleAsync(WorkerBatchSamplePath);

        var traces = SerilogTraceTreeBuilder.SummarizeTraces(events).ToList();

        // batch-1 (3 items) = root + 3 ProcessItem + 3 SendEmail = 7 spans; batch-2 (2 items) = 5; batch-3 (1 item) = 3.
        Assert.Equal(3, traces.Count);
        Assert.Equal([7, 5, 3], traces.Select(t => t.SpanCount).OrderDescending());
    }

    private static Task<List<(long LineNumber, StructuredLogEvent Event)>> ReadSampleAsync() => ReadSampleAsync(SamplePath);

    private static async Task<List<(long LineNumber, StructuredLogEvent Event)>> ReadSampleAsync(string path)
    {
        var events = new List<(long, StructuredLogEvent)>();
        await foreach (var entry in StructuredFileReader.ReadAsync(path, CancellationToken.None))
        {
            events.Add(entry);
        }

        return events;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LogViewer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not locate LogViewer.slnx above '{AppContext.BaseDirectory}'.");
    }
}
