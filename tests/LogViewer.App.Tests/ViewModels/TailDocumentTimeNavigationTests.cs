using LogViewer.App.Models;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentTimeNavigationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private const string Log =
        "2026-09-23 10:00:00.000 INFO start\n" +
        "2026-09-23 10:00:00.250 INFO step one\n" +
        "2026-09-23 10:00:02.000 ERROR failed\n" +
        "   at Worker.Run()\n" +
        "2026-09-23 10:05:00.000 INFO later\n";

    private (MainViewModel Main, TailDocumentViewModel Doc) Open(string content, int expectedLines)
    {
        var (main, _) = MainViewModelFactory.Create();
        var doc = main.OpenPath(_tempDir.CreateFile("t.log", content));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= expectedLines);
        return (main, doc);
    }

    [Fact]
    public void ShowTimeDelta_FillsDeltas_AndReferenceLineChangesTheAnchor()
    {
        var (main, doc) = Open(Log, 5);

        doc.ShowTimeDelta = true;

        Assert.Null(doc.Lines[0].DeltaDisplay);
        Assert.Equal("+250ms", doc.Lines[1].DeltaDisplay);
        Assert.Equal("+1.750s", doc.Lines[2].DeltaDisplay);
        Assert.Null(doc.Lines[3].DeltaDisplay); // continuation line has no timestamp of its own
        Assert.Equal("+4m58s", doc.Lines[4].DeltaDisplay);

        doc.SetTimeReferenceCommand.Execute(doc.Lines[0]);

        Assert.True(doc.HasTimeReference);
        Assert.Equal("+2.000s", doc.Lines[2].DeltaDisplay);
        Assert.Equal("+5m00s", doc.Lines[4].DeltaDisplay);

        doc.ShowTimeDelta = false;
        Assert.All(doc.Lines, l => Assert.Null(l.DeltaDisplay));
        main.Dispose();
    }

    [Fact]
    public void TimeRangeFilter_KeepsContinuationLinesWithTheirEntry()
    {
        var (main, doc) = Open(Log, 5);

        doc.TimeFilterFromText = "10:00:01";
        doc.TimeFilterToText = "10:01";
        doc.ApplyTimeFilterCommand.Execute(null);

        Assert.True(doc.IsTimeFilterActive);
        Assert.True(doc.IsFilterActive);
        Assert.Equal([3L, 4], doc.Lines.Where(doc.PassesTimeFilter).Select(l => l.LineNumber));

        doc.ClearFilterCommand.Execute(null);
        Assert.False(doc.IsTimeFilterActive);
        Assert.All(doc.Lines, l => Assert.True(doc.PassesTimeFilter(l)));
        main.Dispose();
    }

    [Fact]
    public void GoToTime_SelectsTheFirstLineAtOrAfterTheTime_IncludingRelativeInput()
    {
        var (main, doc) = Open(Log, 5);

        doc.GoToTimeText = "10:00:01";
        doc.GoToTimeCommand.Execute(null);
        Assert.Equal(3, doc.SelectedLine?.LineNumber);
        Assert.False(doc.IsFollowingTail);

        doc.GoToTimeText = "-5m"; // relative to the newest line (10:05:00)
        doc.GoToTimeCommand.Execute(null);
        Assert.Equal(1, doc.SelectedLine?.LineNumber);

        doc.GoToTimeText = "not a time";
        doc.GoToTimeCommand.Execute(null);
        Assert.Contains("not a time", doc.StatusMessage);
        main.Dispose();
    }

    [Fact]
    public void GoToTime_OlderThanTheBuffer_OpensTheWholeFileBrowser()
    {
        var start = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
        var content = string.Concat(Enumerable.Range(0, 1500).Select(i => $"{start.AddSeconds(i):yyyy-MM-dd HH:mm:ss} INFO line {i}\n"));
        var (main, doc) = Open(content, 1000);
        FileBrowserTarget? requested = null;
        doc.FileBrowserRequested += t => requested = t;

        doc.GoToTimeText = "2026-09-23 08:00:10";
        doc.GoToTimeCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(start.AddSeconds(10), requested!.Time);
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
