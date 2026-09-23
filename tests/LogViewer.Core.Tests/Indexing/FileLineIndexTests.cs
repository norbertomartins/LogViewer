using System.Text;
using LogViewer.Core.Indexing;
using LogViewer.Core.Search;
using LogViewer.Core.Tailing;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Indexing;

public sealed class FileLineIndexTests
{
    private static string Lines(int count, Func<int, string>? text = null) =>
        string.Concat(Enumerable.Range(1, count).Select(i => (text?.Invoke(i) ?? $"line {i}") + "\n"));

    [Fact]
    public async Task Update_CountsLines_AndReadsAnyRangeAcrossCheckpoints()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(5000));

        var index = new FileLineIndex(file.FilePath, stride: 100);
        var update = await index.UpdateAsync();

        Assert.Equal(LineIndexUpdate.Grew, update);
        Assert.Equal(5000, index.LineCount);

        var lines = index.ReadLines(1234, 5);
        Assert.Equal([1234L, 1235, 1236, 1237, 1238], lines.Select(l => l.LineNumber));
        Assert.Equal("line 1234", lines[0].Text);
        Assert.Equal("line 1238", lines[^1].Text);

        Assert.Equal("line 1", index.ReadLines(1, 1).Single().Text);
        Assert.Equal("line 5000", index.ReadLines(5000, 10).Single().Text);
        Assert.Empty(index.ReadLines(5001, 10));
    }

    [Fact]
    public async Task ReadLines_StripsCrLf_AndIncludesUnterminatedTrailingLine()
    {
        using var file = new TempFileFixture();
        file.WriteAllText("a\r\nb\r\npartial");

        var index = new FileLineIndex(file.FilePath, stride: 1);
        await index.UpdateAsync();

        Assert.Equal(3, index.LineCount);
        Assert.Equal(["a", "b", "partial"], index.ReadLines(1, 10).Select(l => l.Text));
    }

    [Fact]
    public async Task Update_IsIncremental_WhenTheFileGrows()
    {
        using var file = new TempFileFixture();
        file.WriteAllText("one\ntwo\nthr");

        var index = new FileLineIndex(file.FilePath, stride: 2);
        await index.UpdateAsync();
        Assert.Equal(3, index.LineCount);

        file.AppendText("ee\nfour\n");
        var update = await index.UpdateAsync();

        Assert.Equal(LineIndexUpdate.Grew, update);
        Assert.Equal(4, index.LineCount);
        Assert.Equal(["one", "two", "three", "four"], index.ReadLines(1, 10).Select(l => l.Text));
        Assert.Equal(LineIndexUpdate.Unchanged, await index.UpdateAsync());
    }

    [Fact]
    public async Task Update_Rebuilds_WhenTheFileShrinks()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(50));

        var index = new FileLineIndex(file.FilePath, stride: 8);
        await index.UpdateAsync();

        file.WriteAllText("fresh\n");
        var update = await index.UpdateAsync();

        Assert.Equal(LineIndexUpdate.Rebuilt, update);
        Assert.Equal(1, index.LineCount);
        Assert.Equal("fresh", index.ReadLines(1, 5).Single().Text);
    }

    [Fact]
    public async Task ReadLines_HandlesUtf16WithBom()
    {
        using var file = new TempFileFixture();
        File.WriteAllText(file.FilePath, "ação\nözil\n日本\n", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));

        var index = new FileLineIndex(file.FilePath, stride: 1);
        await index.UpdateAsync();

        Assert.Equal(3, index.LineCount);
        Assert.Equal(["ação", "özil", "日本"], index.ReadLines(1, 3).Select(l => l.Text));
    }

    [Fact]
    public async Task LineNumbers_MatchFileTailSource_ForTheSameFile()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(300));

        var index = new FileLineIndex(file.FilePath, stride: 64);
        await index.UpdateAsync();

        var received = new List<TailLine>();
        using var source = new FileTailSource(file.FilePath, new TailSourceOptions { InitialTailLineCount = 10 });
        source.LinesRead += (_, e) => { lock (received) { received.AddRange(e.Lines); } };
        source.Start();

        var tail = received.Last();
        Assert.Equal(tail.Text, index.ReadLines(tail.LineNumber, 1).Single().Text);
    }

    [Fact]
    public async Task FindFirstLineAtOrAfter_BinarySearchesTimestamps()
    {
        using var file = new TempFileFixture();
        var start = new DateTimeOffset(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

        // Every 10th line is an untimestamped continuation line, like a stack-trace frame.
        file.WriteAllText(Lines(2000, i => i % 10 == 0
            ? "   at Some.Frame()"
            : $"{start.AddSeconds(i):yyyy-MM-ddTHH:mm:ssZ} INF message {i}"));

        var index = new FileLineIndex(file.FilePath, stride: 32);
        await index.UpdateAsync();

        Assert.Equal(1234, index.FindFirstLineAtOrAfter(start.AddSeconds(1234), MergedTimestampExtractor.TryExtract));

        // Line 1230 is a continuation line — the next timestamped line wins.
        Assert.Equal(1231, index.FindFirstLineAtOrAfter(start.AddSeconds(1229.5), MergedTimestampExtractor.TryExtract));
        Assert.Equal(1, index.FindFirstLineAtOrAfter(start.AddDays(-1), MergedTimestampExtractor.TryExtract));
        Assert.Null(index.FindFirstLineAtOrAfter(start.AddDays(1), MergedTimestampExtractor.TryExtract));
    }
}

public sealed class FileLineIndexSearchTests
{
    private static async Task<FileLineIndex> IndexOf(TempFileFixture file, int lines, Func<int, string> text)
    {
        file.WriteAllText(string.Concat(Enumerable.Range(1, lines).Select(i => text(i) + "\n")));
        var index = new FileLineIndex(file.FilePath, stride: 64);
        await index.UpdateAsync();
        return index;
    }

    [Fact]
    public async Task FindNext_ScansForwardAndBackward_AcrossPages()
    {
        using var file = new TempFileFixture();
        var index = await IndexOf(file, 1000, i => i % 250 == 0 ? $"ERROR at {i}" : $"info {i}");
        Assert.True(LineMatcher.TryCreate("error", isRegex: false, caseSensitive: false, out var isMatch, out _));

        Assert.Equal(250, index.FindNext(0, forward: true, isMatch)?.LineNumber);
        Assert.Equal(500, index.FindNext(250, forward: true, isMatch)?.LineNumber);
        Assert.Null(index.FindNext(1000, forward: true, isMatch));

        Assert.Equal(750, index.FindNext(1000, forward: false, isMatch)?.LineNumber);
        Assert.Equal(250, index.FindNext(500, forward: false, isMatch)?.LineNumber);
        Assert.Null(index.FindNext(250, forward: false, isMatch));
    }

    [Fact]
    public async Task CountMatches_CountsEveryMatchingLine()
    {
        using var file = new TempFileFixture();
        var index = await IndexOf(file, 700, i => i % 7 == 0 ? $"req-{i} failed" : $"req-{i} ok");
        Assert.True(LineMatcher.TryCreate(@"req-\d+ failed", isRegex: true, caseSensitive: true, out var isMatch, out _));

        Assert.Equal(100, index.CountMatches(isMatch));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("(open", true)]
    public void LineMatcher_RejectsEmptyAndInvalidPatterns(string pattern, bool isRegex)
    {
        Assert.False(LineMatcher.TryCreate(pattern, isRegex, caseSensitive: false, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
