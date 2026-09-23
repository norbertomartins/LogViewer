using System.IO;
using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentNotesTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void EditNote_PromptsAndStoresTheNote_WhichShowsAgainWhenTheFileIsReopened()
    {
        var store = new InMemoryAnnotationStore();
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowTextPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("  root cause  ");
        var path = _tempDir.CreateFile("app.log", "INFO start\nERROR boom\nINFO end\n");

        var (first, _) = MainViewModelFactory.Create(dialogService: dialogs, annotationStore: store);
        var doc = first.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 3);
        Assert.True(doc.CanAnnotate);

        doc.EditNoteCommand.Execute(doc.Lines[1]);

        Assert.Equal("root cause", doc.Lines[1].Note);
        Assert.True(doc.Lines[1].HasNote);
        Assert.Equal([2L], doc.Notes.Select(n => n.LineNumber));
        first.Dispose();

        var (second, _) = MainViewModelFactory.Create(annotationStore: store);
        var reopened = second.OpenPath(path);
        SpinUntil(() => reopened.Lines.Count >= 3);
        Assert.Equal("root cause", reopened.Lines[1].Note);
        Assert.Null(reopened.Lines[0].Note);
        second.Dispose();
    }

    [Fact]
    public void Note_IsHidden_WhenTheLineTextChanged()
    {
        var store = new InMemoryAnnotationStore();
        var path = _tempDir.CreateFile("app.log", "INFO start\nERROR boom\n");
        var (first, _) = MainViewModelFactory.Create(annotationStore: store);
        var doc = first.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 2);
        doc.SetNote(doc.Lines[1], "root cause");
        first.Dispose();

        File.WriteAllText(path, "INFO rotated\nWARN different line now\n");
        var (second, _) = MainViewModelFactory.Create(annotationStore: store);
        var reopened = second.OpenPath(path);
        SpinUntil(() => reopened.Lines.Count >= 2);

        Assert.Null(reopened.Lines[1].Note);
        Assert.Single(reopened.Notes); // still stored — the file might be restored; MCP reports it as lineChanged
        second.Dispose();
    }

    [Fact]
    public void RemoveNote_AndAnEmptyPrompt_BothDeleteTheNote()
    {
        var store = new InMemoryAnnotationStore();
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowTextPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns(string.Empty);
        var path = _tempDir.CreateFile("app.log", "a\nb\n");
        var (main, _) = MainViewModelFactory.Create(dialogService: dialogs, annotationStore: store);
        var doc = main.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 2);

        doc.SetNote(doc.Lines[0], "one");
        doc.SetNote(doc.Lines[1], "two");
        doc.RemoveNoteCommand.Execute(doc.Lines[0]);
        doc.EditNoteCommand.Execute(doc.Lines[1]); // prompt answers "" → removes

        Assert.False(doc.Lines[0].HasNote);
        Assert.False(doc.Lines[1].HasNote);
        Assert.Empty(store.Get(doc.SessionKey));
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
