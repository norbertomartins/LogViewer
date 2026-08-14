using LogViewer.Core.Analysis;
using LogViewer.Core.Configuration;
using LogViewer.Core.Documents;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogDiscoveryToolsTests
{
    public LogDiscoveryToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    private sealed class FakeCatalog(IReadOnlyList<OpenDocumentInfo> documents) : IOpenDocumentCatalog
    {
        public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => documents;
    }

    [Fact]
    public void ListOpenDocuments_ProjectsCatalogEntries()
    {
        var docs = new List<OpenDocumentInfo>
        {
            new(@"C:\logs\a.log", @"C:\logs\a.log", "a.log", TailSourceKind.File, true, false),
        };
        var tools = new LogDiscoveryTools(new FakeCatalog(docs), new FileLineWindowReader());

        var result = tools.ListOpenDocuments();

        var entry = Assert.Single(result);
        Assert.Equal(@"C:\logs\a.log", entry.SourcePath);
        Assert.Equal("File", entry.Kind);
        Assert.True(entry.IsActive);
    }

    [Fact]
    public async Task DescribeSource_MissingFile_ReturnsExistsFalse()
    {
        var tools = new LogDiscoveryTools(new FakeCatalog([]), new FileLineWindowReader());

        var result = await tools.DescribeSource(@"C:\does\not\exist.log", CancellationToken.None);

        Assert.False(result.Exists);
    }

    [Fact]
    public async Task DescribeSource_StructuredFile_DetectsSerilogJsonAndSamplesLines()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            "{\"@t\":\"2026-01-01T00:00:00Z\",\"@m\":\"one\"}",
            "{\"@t\":\"2026-01-01T00:00:01Z\",\"@m\":\"two\"}",
            "{\"@t\":\"2026-01-01T00:00:02Z\",\"@m\":\"three\"}",
            string.Empty,
        ]));

        var tools = new LogDiscoveryTools(new FakeCatalog([]), new FileLineWindowReader());
        var result = await tools.DescribeSource(fixture.FilePath, CancellationToken.None);

        Assert.True(result.Exists);
        Assert.True(result.LooksStructured);
        Assert.Equal(3, result.SampleFirstLines.Count);
    }

    [Fact]
    public async Task ListStructuredProperties_ReturnsDistinctPropertyNamesSorted()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            "{\"@t\":\"2026-01-01T00:00:00Z\",\"@m\":\"one\",\"UserId\":\"1\"}",
            "{\"@t\":\"2026-01-01T00:00:01Z\",\"@m\":\"two\",\"RequestId\":\"r1\"}",
            string.Empty,
        ]));

        var tools = new LogDiscoveryTools(new FakeCatalog([]), new FileLineWindowReader());
        var result = await tools.ListStructuredProperties(fixture.FilePath, sampleSize: 0, CancellationToken.None);

        Assert.Equal(["RequestId", "UserId"], result);
    }
}
