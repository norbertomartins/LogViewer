using LogViewer.App.Controls;
using LogViewer.App.Models;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Indexing;
using LogViewer.Core.Theming;

namespace LogViewer.App.Tests.ViewModels;

public sealed class FileBrowserTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public async Task VirtualFileLineList_ReadsPagesOnDemand()
    {
        var path = _tempDir.CreateFile("big.log", string.Concat(Enumerable.Range(1, 3000).Select(i => $"row {i}\n")));
        var index = new FileLineIndex(path, stride: 100);
        await index.UpdateAsync();

        var list = new VirtualFileLineList(index, _ => null, pageSize: 64, maxCachedPages: 2);
        list.Refresh(contentChanged: true);

        Assert.Equal(3000, list.Count);
        Assert.Equal("row 1", list[0].Text);
        Assert.Equal("row 2500", list[2499].Text);
        Assert.Equal(2499, list.IndexOf(list[2499]));
        Assert.Equal("row 3000", list[2999].Text);
    }

    [Fact]
    public async Task FileBrowserViewModel_OpensAtTheRequestedLine_AndGoesToLineAndTime()
    {
        var start = new DateTimeOffset(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);
        var path = _tempDir.CreateFile("b.log", string.Concat(Enumerable.Range(1, 5000).Select(i => $"{start.AddSeconds(i):yyyy-MM-ddTHH:mm:ssZ} INFO row {i}\n")));
        var shown = new List<long>();

        using var vm = new FileBrowserViewModel(path, "b.log", [], ThemeBaseMode.Light, 12,
            new FileBrowserTarget(1234, null), line => { shown.Add(line); return true; });
        await vm.InitializeAsync();

        Assert.Equal(5000, vm.TotalLines);
        Assert.Equal(1234, vm.SelectedLine?.LineNumber);

        vm.GoToLineText = "42";
        vm.GoToLineCommand.Execute(null);
        Assert.Equal(42, vm.SelectedLine?.LineNumber);

        vm.GoToTimeText = "2026-09-23T09:50:00Z";
        await vm.GoToTimeCommand.ExecuteAsync(null);
        Assert.Equal(3000, vm.SelectedLine?.LineNumber);

        vm.ShowInDocumentCommand.Execute(null);
        Assert.Equal([3000L], shown);
    }

    public void Dispose() => _tempDir.Dispose();
}
