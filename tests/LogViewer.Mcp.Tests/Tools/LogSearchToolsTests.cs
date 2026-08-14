using LogViewer.Core.Analysis;
using LogViewer.Core.Search;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogSearchToolsTests
{
    public LogSearchToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    [Fact]
    public async Task Search_ReturnsMatchingLines_AndReportsTruncation()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n', ["error one", "ok", "error two", "error three", string.Empty]));

        var tools = new LogSearchTools(new FileFullTextSearchService(), new FileLineWindowReader());

        var result = await tools.Search(
            fixture.FilePath, "error", isRegex: false, isCaseSensitive: false, propertyName: null, maxResults: 2, CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task GetLineContext_ClampsRequestedWindowToMaxPerSide()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n', Enumerable.Range(1, 300).Select(i => $"line{i}")));

        var tools = new LogSearchTools(new FileFullTextSearchService(), new FileLineWindowReader());

        var result = await tools.GetLineContext(fixture.FilePath, lineNumber: 150, linesBefore: 1000, linesAfter: 1000, CancellationToken.None);

        Assert.False(result.LineNumberOutOfRange);
        Assert.Equal(201, result.Lines.Count);
    }

    [Fact]
    public async Task PatternOccurrences_FiltersByComputedSignature()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            "{\"@t\":\"2026-01-01T00:00:00Z\",\"@mt\":\"User {Id} logged in\",\"Id\":\"1\"}",
            "{\"@t\":\"2026-01-01T00:00:01Z\",\"@mt\":\"User {Id} logged in\",\"Id\":\"2\"}",
            "{\"@t\":\"2026-01-01T00:00:02Z\",\"@mt\":\"User {Id} logged out\",\"Id\":\"1\"}",
            string.Empty,
        ]));

        var tools = new LogSearchTools(new FileFullTextSearchService(), new FileLineWindowReader());
        var analyzer = new FilePatternFrequencyAnalyzer();
        var patterns = await analyzer.AnalyzeBySignatureAsync(fixture.FilePath, null, 10, CancellationToken.None);
        var loginSignature = patterns.Single(p => p.SampleMessage.Contains("logged in")).Signature;

        var result = await tools.PatternOccurrences(fixture.FilePath, loginSignature, maxResults: 10, CancellationToken.None);

        Assert.Equal(2, result.Results.Count);
    }
}
