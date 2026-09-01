using LogViewer.App.Tests.TestUtilities;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentTimelineTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void Timeline_PlainTextLog_WithLeadingTimestamps_ProducesBins()
    {
        var lines = string.Join('\n', Enumerable.Range(0, 30)
            .Select(i => $"2026-02-15 09:{i:00}:00.000 [INFO] event {i}")) + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", lines));

        SpinUntil(() => doc.Lines.Count >= 30);
        doc.ShowTimeline = true;

        Assert.True(doc.TimelineHasData, $"Lines={doc.Lines.Count} bins={doc.VolumeBins.Count}");
        Assert.NotEmpty(doc.VolumeBins);
        viewModel.Dispose();
    }

    [Fact]
    public void Timeline_NdjsonLog_EvenWithStructuredViewOff_ProducesBins()
    {
        var lines = string.Join('\n', Enumerable.Range(0, 20)
            .Select(i => $"{{\"time\":\"2026-02-15T09:{i:00}:00Z\",\"level\":\"info\",\"msg\":\"e{i}\"}}")) + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.ndjson", lines));
        doc.IsStructuredView = false;

        SpinUntil(() => doc.Lines.Count >= 20);
        doc.ShowTimeline = true;

        Assert.True(doc.TimelineHasData, $"bins={doc.VolumeBins.Count}");
        viewModel.Dispose();
    }

    [Fact]
    public void Timeline_LogWithoutTimestamps_ReportsNoData_DoesNotThrow()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", "just some text\nmore text\nand more\n"));

        SpinUntil(() => doc.Lines.Count >= 3);
        doc.ShowTimeline = true;

        Assert.False(doc.TimelineHasData);
        Assert.Empty(doc.VolumeBins);
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
