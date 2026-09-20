using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TraceTreeViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private const string RootSpan =
        """{"@t":"2026-02-15T09:00:00.300Z","@mt":"GET /orders","@l":"Information","@tr":"trace1","@sp":"root","SpanKind":"Server","SpanStartTimestamp":"2026-02-15T09:00:00.000Z"}""";

    private const string ChildSpan =
        """{"@t":"2026-02-15T09:00:00.100Z","@mt":"SELECT orders","@l":"Information","@tr":"trace1","@sp":"child","ParentSpanId":"root","SpanKind":"Internal","SpanStartTimestamp":"2026-02-15T09:00:00.010Z"}""";

    [Fact]
    public void Construction_BuildsTraceListAndSelectsMostRecentTrace()
    {
        var lines = RootSpan + "\n" + ChildSpan + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.clef", lines));
        SpinUntil(() => doc.Lines.Count >= 2);

        var traceTree = new TraceTreeViewModel(doc, initialTraceId: null);

        var trace = Assert.Single(traceTree.Traces);
        Assert.Equal("trace1", trace.TraceId);
        Assert.Equal(2, trace.SpanCount);
        Assert.Equal(trace, traceTree.SelectedTrace);

        var root = Assert.Single(traceTree.SpanTree);
        Assert.Equal("root", root.SpanId);
        Assert.Equal("GET /orders", root.Name);
        var child = Assert.Single(root.Children);
        Assert.Equal("child", child.SpanId);
        Assert.Equal("SELECT orders", child.Name);

        viewModel.Dispose();
    }

    [Fact]
    public void Construction_WithInitialTraceId_PreselectsThatTrace()
    {
        var otherTrace = """{"@t":"2026-02-15T09:05:00.050Z","@mt":"GET /health","@l":"Information","@tr":"trace2","@sp":"root2","SpanStartTimestamp":"2026-02-15T09:05:00.000Z"}""";
        var lines = RootSpan + "\n" + otherTrace + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.clef", lines));
        SpinUntil(() => doc.Lines.Count >= 2);

        var traceTree = new TraceTreeViewModel(doc, initialTraceId: "trace1");

        Assert.Equal("trace1", traceTree.SelectedTrace?.TraceId);

        viewModel.Dispose();
    }

    [Fact]
    public void JumpToSpan_NavigatesDocumentToSpanLine()
    {
        var lines = RootSpan + "\n" + ChildSpan + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.clef", lines));
        SpinUntil(() => doc.Lines.Count >= 2);

        var traceTree = new TraceTreeViewModel(doc, initialTraceId: null);
        var child = traceTree.SpanTree[0].Children[0];

        traceTree.JumpToSpanCommand.Execute(child);

        Assert.Equal(child.LineNumber, doc.SelectedLine?.LineNumber);

        viewModel.Dispose();
    }

    private static void SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }

    public void Dispose() => _tempDir.Dispose();
}
