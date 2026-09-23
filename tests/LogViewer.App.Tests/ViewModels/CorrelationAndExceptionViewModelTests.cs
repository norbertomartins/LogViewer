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

    [Fact]
    public void ExceptionGroups_LiveMode_RegroupsAsNewLinesArrive_KeepingTheSelection()
    {
        var (main, _) = MainViewModelFactory.Create();
        var path = _tempDir.CreateFile("live.log", "ERROR a\nSystem.InvalidOperationException: x\n   at A.B()\n");
        var doc = main.OpenPath(path);
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 3);

        // Synchronous on purpose: an await could resume on another thread, and TestDispatcher would then pump a
        // different Dispatcher than the one owning the document's tail flush and the panel's live-refresh timer.
        using var vm = new ExceptionGroupsViewModel(doc);
        TestDispatcher.SpinUntil(() => vm.Groups.Count == 1 && !vm.IsAnalyzing);
        var first = Assert.Single(vm.Groups);
        Assert.True(vm.IsLive);

        System.IO.File.AppendAllText(path, "ERROR b\nSystem.TimeoutException: y\n   at C.D()\nERROR c\nSystem.TimeoutException: y\n   at C.D()\n");
        TestDispatcher.SpinUntil(() => vm.Groups.Count == 2 && !vm.IsAnalyzing);

        Assert.Equal(2, vm.Groups.Count);
        Assert.Equal(first.Signature, vm.SelectedGroup?.Signature);
        main.Dispose();
    }

    [Fact]
    public void FilterAllByCorrelation_AppliesTheIdToEveryDocument()
    {
        var (main, _) = MainViewModelFactory.Create();
        var api = main.OpenPath(_tempDir.CreateFile("api.log", "INFO start request_id=req-42\nINFO other request_id=req-7\n"));
        var db = main.OpenPath(_tempDir.CreateFile("db.log", "DEBUG query for req-42 took 5ms\n"));
        var mail = main.OpenPath(_tempDir.CreateFile("mail.log", "INFO nothing related\n"));
        TestDispatcher.SpinUntil(() => api.Lines.Count >= 2 && db.Lines.Count >= 1 && mail.Lines.Count >= 1);

        api.FilterAllByCorrelationCommand.Execute(api.Lines[0].CorrelationIds[0]);

        Assert.All([api, db, mail], d => Assert.Equal("req-42", d.CorrelationFilter?.Value));
        Assert.Single(db.Lines, db.PassesCorrelationFilter);
        Assert.Contains("2", main.StatusMessage); // 2 of 3 documents have matching lines
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
