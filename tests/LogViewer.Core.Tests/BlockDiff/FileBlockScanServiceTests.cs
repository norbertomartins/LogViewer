using LogViewer.Core.BlockDiff;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class FileBlockScanServiceTests
{
    [Fact]
    public async Task ScanAsync_ByCorrelationField_GroupsInterleavedLinesByTraceId()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "start", "abc"),
            Clef("2026-01-01T00:00:01Z", "start", "xyz"),
            Clef("2026-01-01T00:00:02Z", "middle", "abc"),
            Clef("2026-01-01T00:00:03Z", "middle", "xyz"),
            Clef("2026-01-01T00:00:04Z", "end", "abc"),
            Clef("2026-01-01T00:00:05Z", "end", "xyz"),
            string.Empty,
        ]));

        var service = new FileBlockScanService();
        var options = BlockDetectionOptions.ByCorrelation("TraceId");

        var blocks = new List<LogBlock>();
        await foreach (var block in service.ScanAsync(fixture.FilePath, options, CancellationToken.None))
        {
            blocks.Add(block);
        }

        Assert.Equal(2, blocks.Count);
        var abcBlock = Assert.Single(blocks, b => b.CorrelationValue == "abc");
        Assert.Equal([1L, 3L, 5L], abcBlock.Lines.Select(l => l.LineNumber));
    }

    [Fact]
    public async Task ScanAsync_ByProximity_SplitsOnTimeGap()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "op1 start", null),
            Clef("2026-01-01T00:00:01Z", "op1 end", null),
            Clef("2026-01-01T00:05:00Z", "op2 start", null),
            Clef("2026-01-01T00:05:01Z", "op2 end", null),
            string.Empty,
        ]));

        var service = new FileBlockScanService();
        var options = BlockDetectionOptions.ByProximity(maxGap: TimeSpan.FromSeconds(2));

        var blocks = new List<LogBlock>();
        await foreach (var block in service.ScanAsync(fixture.FilePath, options, CancellationToken.None))
        {
            blocks.Add(block);
        }

        Assert.Equal(2, blocks.Count);
        Assert.Equal(2, blocks[0].Lines.Count);
        Assert.Equal(2, blocks[1].Lines.Count);
    }

    private static string Clef(string timestamp, string message, string? traceId)
    {
        var traceIdJson = traceId is null ? string.Empty : $",\"TraceId\":\"{traceId}\"";
        return $"{{\"@t\":\"{timestamp}\",\"@m\":\"{message}\"{traceIdJson}}}";
    }
}
