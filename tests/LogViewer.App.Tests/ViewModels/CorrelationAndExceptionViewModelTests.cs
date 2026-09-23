using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class CorrelationAndExceptionViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private (MainViewModel Main, TailDocumentViewModel Doc) Open(string content, int expectedLines)
    {
        var (main, _) = MainViewModelFactory.Create();
        var doc = main.OpenPath(_tempDir.CreateFile("t.log", content));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= expectedLines);
        return (main, doc);
    }

    [Fact]
    public void CorrelationFilter_MatchesAnyLineContainingTheId()
    {
        var (main, doc) = Open(
            "INFO start request_id=req-42\nINFO unrelated request_id=req-7\nWARN slow call for req-42\n", 3);

        var id = Assert.Single(doc.Lines[0].CorrelationIds);
        Assert.Equal("req-42", id.Value);

        doc.FilterByCorrelationCommand.Execute(id);

        Assert.True(doc.IsCorrelationFilterActive);
        Assert.Contains("req-42", doc.FilterStatusText);
        Assert.Equal([1L, 3], doc.Lines.Where(doc.PassesCorrelationFilter).Select(l => l.LineNumber));

        doc.ClearFilterCommand.Execute(null);
        Assert.False(doc.IsCorrelationFilterActive);
        main.Dispose();
    }

    [Fact]
    public async Task ExceptionGroups_BufferScope_GroupsTracesFromTheDocument()
    {
        var (main, _) = MainViewModelFactory.Create();
        var doc = main.OpenPath(_tempDir.CreateFile("e.log",
            "ERROR a\nSystem.InvalidOperationException: x 1\n   at A.B()\n" +
            "ERROR b\nSystem.InvalidOperationException: x 2\n   at A.B()\n"));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 6);

        using var vm = new ExceptionGroupsViewModel(doc);
        await vm.AnalyzeCommand.ExecuteAsync(null);

        var group = Assert.Single(vm.Groups);
        Assert.Equal(2, group.Count);
        Assert.Same(group, vm.SelectedGroup);
        Assert.True(vm.CanScanWholeFile);

        vm.BookmarkOccurrencesCommand.Execute(null);
        Assert.True(doc.Lines.Single(l => l.LineNumber == 2).IsBookmarked);
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
