using LogViewer.Core.BlockDiff;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogBlockToolsTests
{
    public LogBlockToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    [Fact]
    public async Task ScanBlocks_ByCorrelation_ReportsErrorFlagPerBlock()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "start", "abc"),
            Clef("2026-01-01T00:00:01Z", "Error", "boom", "abc"),
            Clef("2026-01-01T00:00:02Z", "Information", "start", "xyz"),
            Clef("2026-01-01T00:00:03Z", "Information", "end", "xyz"),
            string.Empty,
        ]));

        var scanService = new FileBlockScanService();
        var tools = new LogBlockTools(scanService, new SimilarBlockFinder(scanService));

        var result = await tools.ScanBlocks(fixture.FilePath, "correlation", "TraceId", null, null, maxBlocks: 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        var abcBlock = Assert.Single(result, b => b.CorrelationValue == "abc");
        Assert.True(abcBlock.HasErrorOrAbove);
        var xyzBlock = Assert.Single(result, b => b.CorrelationValue == "xyz");
        Assert.False(xyzBlock.HasErrorOrAbove);
    }

    [Fact]
    public async Task ScanBlocks_CorrelationStrategyWithoutField_Throws()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Clef("2026-01-01T00:00:00Z", "Information", "start", "abc") + "\n");

        var scanService = new FileBlockScanService();
        var tools = new LogBlockTools(scanService, new SimilarBlockFinder(scanService));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.ScanBlocks(fixture.FilePath, "correlation", null, null, null, maxBlocks: 10, CancellationToken.None));
    }

    [Fact]
    public async Task FindSimilarBlocks_ReturnsRankedMatches()
    {
        using var anchorFixture = new TempFileFixture("anchor.log");
        anchorFixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "start", "abc"),
            Clef("2026-01-01T00:00:01Z", "Information", "end", "abc"),
            string.Empty,
        ]));

        using var targetFixture = new TempFileFixture("target.log");
        targetFixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "start", "xyz"),
            Clef("2026-01-01T00:00:01Z", "Information", "end", "xyz"),
            string.Empty,
        ]));

        var scanService = new FileBlockScanService();
        var tools = new LogBlockTools(scanService, new SimilarBlockFinder(scanService));

        var result = await tools.FindSimilarBlocks(
            anchorFixture.FilePath, anchorLineNumber: 1, targetFixture.FilePath, "correlation", "TraceId", null, null, topN: 5, CancellationToken.None);

        var match = Assert.Single(result);
        Assert.Equal("xyz", match.Block.CorrelationValue);
        Assert.True(match.Score > 0);
    }

    private static string Clef(string timestamp, string level, string message, string traceId) =>
        $"{{\"@t\":\"{timestamp}\",\"@l\":\"{level}\",\"@m\":\"{message}\",\"TraceId\":\"{traceId}\"}}";
}
