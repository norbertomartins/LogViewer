using LogViewer.Core.Search;
using LogViewer.Core.Structured;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Search;

public sealed class FileFullTextSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_PlainSubstring_FindsMatchingLinesInOrder()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("info: starting\nerror: disk full\ninfo: retrying\nerror: timeout\n");
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, "error", isRegex: false, isCaseSensitive: false, propertyName: null, CancellationToken.None));

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results[0].LineNumber);
        Assert.Equal("error: disk full", results[0].Text);
        Assert.Equal(4, results[1].LineNumber);
        Assert.Equal("error: timeout", results[1].Text);
    }

    [Fact]
    public async Task SearchAsync_CaseSensitive_DoesNotMatchDifferentCasing()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("ERROR: one\nerror: two\n");
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, "ERROR", isRegex: false, isCaseSensitive: true, propertyName: null, CancellationToken.None));

        Assert.Single(results);
        Assert.Equal("ERROR: one", results[0].Text);
    }

    [Fact]
    public async Task SearchAsync_Regex_MatchesPattern()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("code=200\ncode=404\ncode=500\n");
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, @"code=(4|5)\d\d", isRegex: true, isCaseSensitive: false, propertyName: null, CancellationToken.None));

        Assert.Equal(2, results.Count);
        Assert.Equal("code=404", results[0].Text);
        Assert.Equal("code=500", results[1].Text);
    }

    [Fact]
    public async Task SearchAsync_Cancelled_StopsYieldingResults()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Concat(Enumerable.Repeat("match line\n", 10_000)));
        var service = new FileFullTextSearchService();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            var count = 0;
            await foreach (var _ in service.SearchAsync(fixture.FilePath, "match", isRegex: false, isCaseSensitive: false, propertyName: null, cts.Token))
            {
                count++;
                if (count == 1)
                {
                    cts.Cancel();
                }
            }
        });
    }

    [Fact]
    public async Task SearchAsync_WithPropertyName_OnlyMatchesThatPropertysValue()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""a"",""@l"":""Information"",""RequestId"":""abc""}",
            @"{""@t"":""2026-01-01T00:00:01Z"",""@mt"":""b"",""@l"":""Error"",""RequestId"":""def""}",
            @"{""@t"":""2026-01-01T00:00:02Z"",""@mt"":""c"",""@l"":""Error"",""RequestId"":""abc""}",
            "plain text line mentioning abc",
            "",
        ]));
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, "abc", isRegex: false, isCaseSensitive: false, propertyName: "RequestId", CancellationToken.None));

        Assert.Equal(2, results.Count);
        Assert.Equal(1, results[0].LineNumber);
        Assert.Equal(3, results[1].LineNumber);
    }

    [Fact]
    public async Task SearchAsync_WithPropertyName_WellKnownLevelField_Matches()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""a"",""@l"":""Information""}",
            @"{""@t"":""2026-01-01T00:00:01Z"",""@mt"":""b"",""@l"":""Error""}",
            "",
        ]));
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, "Error", isRegex: false, isCaseSensitive: false, propertyName: StructuredFieldResolver.LevelField, CancellationToken.None));

        Assert.Single(results);
        Assert.Equal(2, results[0].LineNumber);
    }

    [Fact]
    public async Task SearchAsync_WithPropertyName_NonJsonLines_NeverMatch()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("plain text line one\nplain text line two\n");
        var service = new FileFullTextSearchService();

        var results = await CollectAsync(service.SearchAsync(fixture.FilePath, "line", isRegex: false, isCaseSensitive: false, propertyName: "RequestId", CancellationToken.None));

        Assert.Empty(results);
    }

    private static async Task<List<SearchResult>> CollectAsync(IAsyncEnumerable<SearchResult> source)
    {
        var results = new List<SearchResult>();
        await foreach (var item in source)
        {
            results.Add(item);
        }

        return results;
    }
}
