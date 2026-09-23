using LogViewer.Core.Documents;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogAnnotationWriteToolsTests
{
    public LogAnnotationWriteToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    private sealed class RecordingWriter(string? error = null) : IDocumentAnnotationWriter
    {
        public List<string> Calls { get; } = [];

        public string? AddBookmark(string sourcePath, long lineNumber)
        {
            Calls.Add($"bookmark {lineNumber}");
            return error;
        }

        public string? AddNote(string sourcePath, long lineNumber, string lineText, string note)
        {
            Calls.Add($"note {lineNumber} [{lineText}] {note}");
            return error;
        }
    }

    [Fact]
    public async Task AddBookmark_WritesAndReturnsTheLineText()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("first\nsecond\nthird\n");
        var writer = new RecordingWriter();

        var result = await new LogAnnotationWriteTools(writer).AddBookmark(fixture.FilePath, 2, CancellationToken.None);

        Assert.True(result.Written);
        Assert.Equal("second", result.LineText);
        Assert.Null(result.Error);
        Assert.Equal(["bookmark 2"], writer.Calls);
    }

    [Fact]
    public async Task AddNote_PrefixesTheNoteAndPassesTheLineTextItIsTiedTo()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("first\nsecond\n");
        var writer = new RecordingWriter();

        var result = await new LogAnnotationWriteTools(writer).AddNote(fixture.FilePath, 1, "  first failure\nof the storm ", CancellationToken.None);

        Assert.True(result.Written);
        Assert.Equal(["note 1 [first] [AI] first failure of the storm"], writer.Calls);
    }

    [Fact]
    public async Task AddNote_CutsLongNotes()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("first\n");
        var writer = new RecordingWriter();

        await new LogAnnotationWriteTools(writer).AddNote(fixture.FilePath, 1, new string('x', 2000), CancellationToken.None);

        var call = Assert.Single(writer.Calls);
        Assert.EndsWith(new string('x', LogAnnotationWriteTools.MaxNoteLength) + "…", call);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task OutOfRangeLines_AreRejectedWithoutWriting(long lineNumber)
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("a\nb\nc\n");
        var writer = new RecordingWriter();
        var tools = new LogAnnotationWriteTools(writer);

        var bookmark = await tools.AddBookmark(fixture.FilePath, lineNumber, CancellationToken.None);
        var note = await tools.AddNote(fixture.FilePath, lineNumber, "x", CancellationToken.None);

        Assert.False(bookmark.Written);
        Assert.Contains("out of range", bookmark.Error);
        Assert.False(note.Written);
        Assert.Empty(writer.Calls);
    }

    [Fact]
    public async Task EmptyNotes_MissingFiles_AndWriterErrors_AreReported()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("a\n");

        var empty = await new LogAnnotationWriteTools(new RecordingWriter()).AddNote(fixture.FilePath, 1, "   ", CancellationToken.None);
        var missing = await new LogAnnotationWriteTools(new RecordingWriter()).AddBookmark(fixture.FilePath + ".nope", 1, CancellationToken.None);
        var notOpen = await new LogAnnotationWriteTools(new RecordingWriter("not open")).AddBookmark(fixture.FilePath, 1, CancellationToken.None);

        Assert.Equal("The note is empty.", empty.Error);
        Assert.StartsWith("File not found", missing.Error);
        Assert.False(notOpen.Written);
        Assert.Equal("not open", notOpen.Error);
    }
}
