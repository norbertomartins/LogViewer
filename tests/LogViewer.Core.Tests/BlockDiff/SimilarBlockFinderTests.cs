using System.Runtime.CompilerServices;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class SimilarBlockFinderTests
{
    private static LogBlockLine Line(long lineNumber, string signature) =>
        new(lineNumber, signature, new StructuredLogEvent(null, "Information", null, signature, null, new Dictionary<string, string>()));

    private sealed class FakeScanService(IReadOnlyList<LogBlock> blocks) : IBlockScanService
    {
        public async IAsyncEnumerable<LogBlock> ScanAsync(
            string targetPath, BlockDetectionOptions options, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var block in blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return block;
            }
        }
    }

    [Fact]
    public async Task FindBestMatchesAsync_RanksHighestScoreFirst_AndCapsAtTopN()
    {
        var anchor = new LogBlock([Line(1, "A"), Line(2, "B"), Line(3, "C")], null, null, "anchor");

        var candidates = new List<LogBlock>
        {
            new([Line(10, "A"), Line(11, "B"), Line(12, "C")], null, null, "perfect"),
            new([Line(20, "A"), Line(21, "X"), Line(22, "C")], null, null, "partial"),
            new([Line(30, "X"), Line(31, "Y"), Line(32, "Z")], null, null, "nomatch"),
            new([Line(40, "A"), Line(41, "B")], null, null, "close"),
        };

        var finder = new SimilarBlockFinder(new FakeScanService(candidates));

        var results = await finder.FindBestMatchesAsync(
            anchor, "target.log", BlockDetectionOptions.ByProximity(), topN: 2, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("perfect", results[0].Block.SourceDescription);
        Assert.Equal(1.0, results[0].Score);
        Assert.True(results[0].Score >= results[1].Score);
        Assert.DoesNotContain(results, r => r.Block.SourceDescription is "nomatch" or "partial");
    }

    [Fact]
    public async Task FindBestMatchesAsync_NoOverlap_ReturnsEmpty()
    {
        var anchor = new LogBlock([Line(1, "A")], null, null, "anchor");
        var candidates = new List<LogBlock> { new([Line(10, "Z")], null, null, "unrelated") };

        var finder = new SimilarBlockFinder(new FakeScanService(candidates));

        var results = await finder.FindBestMatchesAsync(
            anchor, "target.log", BlockDetectionOptions.ByProximity(), topN: 5, CancellationToken.None);

        Assert.Empty(results);
    }
}
