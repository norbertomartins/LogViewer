using LogViewer.Core.BlockDiff;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class BlockLookupTests
{
    [Fact]
    public async Task FindBlockContainingLineAsync_ReturnsBlockThatContainsTheLine()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "start", "abc"),
            Clef("2026-01-01T00:00:01Z", "start", "xyz"),
            Clef("2026-01-01T00:00:02Z", "end", "abc"),
            Clef("2026-01-01T00:00:03Z", "end", "xyz"),
            string.Empty,
        ]));

        var service = new FileBlockScanService();
        var options = BlockDetectionOptions.ByCorrelation("TraceId");

        var block = await BlockLookup.FindBlockContainingLineAsync(service, fixture.FilePath, options, lineNumber: 3, CancellationToken.None);

        Assert.NotNull(block);
        Assert.Equal("abc", block!.CorrelationValue);
        Assert.Contains(block.Lines, l => l.LineNumber == 3);
    }

    [Fact]
    public async Task FindBlockContainingLineAsync_LineNotFound_ReturnsNull()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Clef("2026-01-01T00:00:00Z", "start", "abc") + "\n");

        var service = new FileBlockScanService();
        var options = BlockDetectionOptions.ByCorrelation("TraceId");

        var block = await BlockLookup.FindBlockContainingLineAsync(service, fixture.FilePath, options, lineNumber: 999, CancellationToken.None);

        Assert.Null(block);
    }

    private static string Clef(string timestamp, string message, string? traceId)
    {
        var traceIdJson = traceId is null ? string.Empty : $",\"TraceId\":\"{traceId}\"";
        return $"{{\"@t\":\"{timestamp}\",\"@m\":\"{message}\"{traceIdJson}}}";
    }
}
