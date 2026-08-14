using LogViewer.Core.Analysis;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Analysis;

public sealed class FileLineWindowReaderTests
{
    [Fact]
    public async Task ReadAsync_ReturnsWindowAroundCenterLine()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line{i}")));

        var reader = new FileLineWindowReader();
        var result = await reader.ReadAsync(fixture.FilePath, centerLineNumber: 5, linesBefore: 2, linesAfter: 2, CancellationToken.None);

        Assert.False(result.LineNumberOutOfRange);
        Assert.Equal([3L, 4L, 5L, 6L, 7L], result.Lines.Select(l => l.LineNumber));
        Assert.Equal("line5", result.Lines.Single(l => l.LineNumber == 5).Text);
    }

    [Fact]
    public async Task ReadAsync_ClampsWindowStartAtLineOne()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n', Enumerable.Range(1, 5).Select(i => $"line{i}")));

        var reader = new FileLineWindowReader();
        var result = await reader.ReadAsync(fixture.FilePath, centerLineNumber: 2, linesBefore: 10, linesAfter: 1, CancellationToken.None);

        Assert.Equal(1L, result.Lines.First().LineNumber);
    }

    [Fact]
    public async Task ReadAsync_LineNumberBeyondEndOfFile_ReportsOutOfRange()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n', Enumerable.Range(1, 3).Select(i => $"line{i}")));

        var reader = new FileLineWindowReader();
        var result = await reader.ReadAsync(fixture.FilePath, centerLineNumber: 100, linesBefore: 2, linesAfter: 2, CancellationToken.None);

        Assert.True(result.LineNumberOutOfRange);
        Assert.Empty(result.Lines);
    }
}
