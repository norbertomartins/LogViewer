using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Search;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class SearchViewModelBookmarkTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void BookmarkSelected_TogglesBookmarkOnTheResultsLine()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\ntwo\nthree\n"));
        SpinUntil(() => doc.Lines.Count >= 3);

        var search = new SearchViewModel(doc, Substitute.For<IFullTextSearchService>(), Substitute.For<IEventLogSearchService>());
        search.Results.Add(new SearchResult(2, 0, "two"));
        search.SelectedResult = search.Results[0];

        search.BookmarkSelectedCommand.Execute(null);

        Assert.True(doc.Lines.FindByLineNumber(2)!.IsBookmarked);

        search.BookmarkSelectedCommand.Execute(null);

        Assert.False(doc.Lines.FindByLineNumber(2)!.IsBookmarked);
        viewModel.Dispose();
    }

    [Fact]
    public void BookmarkSelected_WithNoSelection_DoesNothing()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("b.log", "one\n"));
        SpinUntil(() => doc.Lines.Count >= 1);

        var search = new SearchViewModel(doc, Substitute.For<IFullTextSearchService>(), Substitute.For<IEventLogSearchService>());

        search.BookmarkSelectedCommand.Execute(null);

        Assert.False(doc.Lines.FindByLineNumber(1)!.IsBookmarked);
        viewModel.Dispose();
    }

    [Fact]
    public void BookmarkAll_BookmarksEveryResultLine_WithoutTogglingAlreadyBookmarkedOnes()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("c.log", "one\ntwo\nthree\n"));
        SpinUntil(() => doc.Lines.Count >= 3);
        doc.SelectedLine = doc.Lines.FindByLineNumber(1);
        doc.ToggleBookmarkCommand.Execute(null);
        Assert.True(doc.Lines.FindByLineNumber(1)!.IsBookmarked);

        var search = new SearchViewModel(doc, Substitute.For<IFullTextSearchService>(), Substitute.For<IEventLogSearchService>());
        search.Results.Add(new SearchResult(1, 0, "one"));
        search.Results.Add(new SearchResult(3, 0, "three"));

        search.BookmarkAllCommand.Execute(null);

        Assert.True(doc.Lines.FindByLineNumber(1)!.IsBookmarked);
        Assert.True(doc.Lines.FindByLineNumber(3)!.IsBookmarked);
        viewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();

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
}
