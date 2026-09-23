using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;

namespace LogViewer.App.Tests.Services;

public sealed class WpfOpenDocumentCatalogTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void AddBookmark_BookmarksTheLineOfTheOpenFile_AndReportsItInTheStatusBar()
    {
        var path = _tempDir.CreateFile("app.log", "INFO start\nERROR boom\nINFO end\n");
        var (main, _) = MainViewModelFactory.Create(annotationStore: new InMemoryAnnotationStore());
        var doc = main.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 3);
        var catalog = new WpfOpenDocumentCatalog(main);

        var error = catalog.AddBookmark(path, 2);

        Assert.Null(error);
        Assert.Equal([2L], doc.BookmarkedLineNumbers);
        Assert.True(doc.Lines[1].IsBookmarked);
        Assert.Contains("2", main.StatusMessage);
        main.Dispose();
    }

    [Fact]
    public void AddNote_AppendsToTheUsersNote_InsteadOfReplacingIt()
    {
        var store = new InMemoryAnnotationStore();
        var path = _tempDir.CreateFile("app.log", "INFO start\nERROR boom\n");
        var (main, _) = MainViewModelFactory.Create(annotationStore: store);
        var doc = main.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 2);
        doc.SetNote(doc.Lines[1], "root cause");
        var catalog = new WpfOpenDocumentCatalog(main);

        Assert.Null(catalog.AddNote(path, 2, "ERROR boom", "[AI] first failure"));
        Assert.Null(catalog.AddNote(path, 1, "INFO start", "[AI] deploy"));

        Assert.Equal("root cause · [AI] first failure", doc.Lines[1].Note);
        Assert.Equal("[AI] deploy", doc.Lines[0].Note);
        Assert.Equal(2, store.Get(doc.SessionKey).Count);
        main.Dispose();
    }

    [Fact]
    public void Writes_ToAFileThatIsNotOpen_AreRefused()
    {
        var open = _tempDir.CreateFile("open.log", "a\n");
        var closed = _tempDir.CreateFile("closed.log", "a\n");
        var (main, _) = MainViewModelFactory.Create(annotationStore: new InMemoryAnnotationStore());
        var doc = main.OpenPath(open);
        SpinUntil(() => doc.Lines.Count >= 1);
        var catalog = new WpfOpenDocumentCatalog(main);

        Assert.Contains("not open", catalog.AddBookmark(closed, 1));
        Assert.Contains("not open", catalog.AddNote(closed, 1, "a", "[AI] x"));
        Assert.Empty(doc.BookmarkedLineNumbers);
        main.Dispose();
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
