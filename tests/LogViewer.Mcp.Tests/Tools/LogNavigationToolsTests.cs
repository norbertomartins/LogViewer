using LogViewer.Core.Annotations;
using LogViewer.Core.Configuration;
using LogViewer.Core.Documents;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogNavigationToolsTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    public LogNavigationToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    private sealed class FakeCatalog(IReadOnlyList<OpenDocumentInfo> documents) : IOpenDocumentCatalog
    {
        public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => documents;
    }

    /// <summary>One line per minute from 10:00, plus an untimestamped stack frame after every 10th line.</summary>
    private static string MinuteLog(int minutes) => string.Concat(Enumerable.Range(0, minutes).Select(i =>
        $"{Start.AddMinutes(i):yyyy-MM-dd HH:mm:ss} INFO tick {i}\n" + (i % 10 == 9 ? "   at Worker.Run()\n" : string.Empty)));

    [Fact]
    public async Task QueryTimeRange_AbsoluteRange_IncludesContinuationLines()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(MinuteLog(120));
        var tools = new LogNavigationTools(new FakeCatalog([]));

        var result = await tools.QueryTimeRange(fixture.FilePath, "2026-09-23 10:08", "10:10", 100, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(
            ["tick 8", "tick 9", "   at Worker.Run()", "tick 10"],
            result.Lines.Select(l => l.Text.Contains("tick") ? l.Text[(l.Text.IndexOf("tick", StringComparison.Ordinal))..] : l.Text));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task QueryTimeRange_RelativeToTheNewestLine_AndTruncates()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(MinuteLog(120));
        var tools = new LogNavigationTools(new FakeCatalog([]));

        var lastFive = await tools.QueryTimeRange(fixture.FilePath, "-4m", null, 100, CancellationToken.None);
        Assert.Equal(5, lastFive.Lines.Count(l => l.Text.Contains("tick", StringComparison.Ordinal)));
        Assert.EndsWith("tick 119", lastFive.Lines[^2].Text); // followed by its stack frame
        Assert.Null(lastFive.To);

        var capped = await tools.QueryTimeRange(fixture.FilePath, "10:00", null, 3, CancellationToken.None);
        Assert.Equal(3, capped.Lines.Count);
        Assert.True(capped.Truncated);

        var bad = await tools.QueryTimeRange(fixture.FilePath, "whenever", null, 3, CancellationToken.None);
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task GetNewLinesSince_PagesThroughTheFile_AndFollowsAppends()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("a\nb\nc\nd\ne\n");
        var tools = new LogNavigationTools(new FakeCatalog([]));

        var first = await tools.GetNewLinesSince(fixture.FilePath, 0, 3, CancellationToken.None);
        Assert.Equal(["a", "b", "c"], first.Lines.Select(l => l.Text));
        Assert.Equal(3, first.NextCursor);
        Assert.True(first.HasMore);

        var rest = await tools.GetNewLinesSince(fixture.FilePath, first.NextCursor, 10, CancellationToken.None);
        Assert.Equal(["d", "e"], rest.Lines.Select(l => l.Text));
        Assert.False(rest.HasMore);

        File.AppendAllText(fixture.FilePath, "f\n");
        var appended = await tools.GetNewLinesSince(fixture.FilePath, rest.NextCursor, 10, CancellationToken.None);
        Assert.Equal(["f"], appended.Lines.Select(l => l.Text));

        var tail = await tools.GetNewLinesSince(fixture.FilePath, -1, 2, CancellationToken.None);
        Assert.Equal(["e", "f"], tail.Lines.Select(l => l.Text));

        fixture.WriteAllText("new\n"); // truncated/rotated
        var reset = await tools.GetNewLinesSince(fixture.FilePath, appended.NextCursor, 10, CancellationToken.None);
        Assert.True(reset.FileWasReset);
        Assert.Equal(["new"], reset.Lines.Select(l => l.Text));
    }

    [Fact]
    public async Task GetNotes_ReturnsEachNoteWithItsLineText()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("first\nsecond\nthird\n");
        var notes = new[]
        {
            new LineAnnotation(3, LineAnnotationStore.HashText("third"), "root cause", Start),
            new LineAnnotation(1, LineAnnotationStore.HashText("first"), "deploy started", Start),
            new LineAnnotation(2, LineAnnotationStore.HashText("what line 2 used to say"), "old note", Start),
        };
        var docs = new List<OpenDocumentInfo>
        {
            new(fixture.FilePath, fixture.FilePath, "b.log", TailSourceKind.File, true, false, Notes: notes),
            new("other", null, "no notes", TailSourceKind.Process, false, false),
        };
        var tools = new LogNavigationTools(new FakeCatalog(docs));

        var result = await tools.GetNotes(10, CancellationToken.None);

        var doc = Assert.Single(result);
        Assert.Equal(["deploy started", "old note", "root cause"], doc.Notes.Select(n => n.Note));
        Assert.Equal(["first", "second", "third"], doc.Notes.Select(n => n.Text));
        Assert.Equal([false, true, false], doc.Notes.Select(n => n.LineChanged));
    }

    [Fact]
    public async Task GetBookmarks_ReturnsBookmarkedLinesWithText()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("first\nsecond\nthird\n");
        var docs = new List<OpenDocumentInfo>
        {
            new(fixture.FilePath, fixture.FilePath, "b.log", TailSourceKind.File, true, false, [1, 3]),
            new("other", null, "no bookmarks", TailSourceKind.Process, false, false),
        };
        var tools = new LogNavigationTools(new FakeCatalog(docs));

        var result = await tools.GetBookmarks(10, CancellationToken.None);

        var doc = Assert.Single(result);
        Assert.Equal(["first", "third"], doc.Bookmarks.Select(b => b.Text));
        Assert.Equal([1L, 3], doc.Bookmarks.Select(b => b.LineNumber));
    }
}
