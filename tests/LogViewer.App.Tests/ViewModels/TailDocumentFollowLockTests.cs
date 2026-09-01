using LogViewer.App.Tests.TestUtilities;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentFollowLockTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void ScrollingAwayFromEnd_PausesFollow()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\ntwo\n"));
        Assert.True(doc.IsFollowingTail);

        doc.NotifyUserScrolledAwayFromEnd();

        Assert.False(doc.IsFollowingTail);
        viewModel.Dispose();
    }

    [Fact]
    public void ScrollingBackToEnd_ResumesFollow_AndClearsUnseenCount()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("b.log", "one\n"));
        doc.NotifyUserScrolledAwayFromEnd();
        doc.UnseenLineCount = 5;

        doc.NotifyUserScrolledToEnd();

        Assert.True(doc.IsFollowingTail);
        Assert.Equal(0, doc.UnseenLineCount);
        viewModel.Dispose();
    }

    [Fact]
    public void ResumeFollowBanner_ReflectsCount()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("c.log", "one\n"));
        doc.IsFollowingTail = false;

        doc.UnseenLineCount = 1;
        Assert.Equal("⤓ 1 new line — resume follow", doc.ResumeFollowBanner);

        doc.UnseenLineCount = 42;
        Assert.Equal("⤓ 42 new lines — resume follow", doc.ResumeFollowBanner);

        viewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
