using LogViewer.App.Tests.TestUtilities;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentClearViewTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void ClearView_HidesExistingLines_ButNotFutureOnes()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\ntwo\n"));
        SpinUntil(() => doc.Lines.Count >= 2);
        Assert.False(doc.IsHidingPastLines);

        doc.ClearViewCommand.Execute(null);

        Assert.True(doc.IsHidingPastLines);
        Assert.Equal(doc.Lines[^1].LineNumber + 1, doc.HideBeforeLineNumber);
        viewModel.Dispose();
    }

    [Fact]
    public void ShowHiddenLines_ClearsTheHiddenMarker()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("b.log", "one\n"));
        SpinUntil(() => doc.Lines.Count >= 1);
        doc.ClearViewCommand.Execute(null);
        Assert.True(doc.IsHidingPastLines);

        doc.ShowHiddenLinesCommand.Execute(null);

        Assert.False(doc.IsHidingPastLines);
        Assert.Null(doc.HideBeforeLineNumber);
        viewModel.Dispose();
    }

    [Fact]
    public void HideLinesAboveSelected_UsesSelectedLineNumber()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("c.log", "one\ntwo\nthree\n"));
        SpinUntil(() => doc.Lines.Count >= 3);
        doc.SelectedLine = doc.Lines[1];

        doc.HideLinesAboveSelectedCommand.Execute(null);

        Assert.Equal(doc.Lines[1].LineNumber, doc.HideBeforeLineNumber);
        viewModel.Dispose();
    }

    [Fact]
    public void GoToStart_PausesFollow()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("d.log", "one\n"));
        Assert.True(doc.IsFollowingTail);

        doc.GoToStartCommand.Execute(null);

        Assert.False(doc.IsFollowingTail);
        viewModel.Dispose();
    }

    [Fact]
    public void GoToEnd_ResumesFollow()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("e.log", "one\n"));
        doc.IsFollowingTail = false;
        doc.HasUnseenChanges = true;

        doc.GoToEndCommand.Execute(null);

        Assert.True(doc.IsFollowingTail);
        Assert.False(doc.HasUnseenChanges);
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
