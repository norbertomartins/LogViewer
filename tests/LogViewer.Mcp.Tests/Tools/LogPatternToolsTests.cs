using System.Text.Json;
using LogViewer.Core.Analysis;
using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogPatternToolsTests
{
    public LogPatternToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    [Fact]
    public async Task TopErrorSources_DefaultsToSourceContextAndErrorLevel()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            "{\"@t\":\"2026-01-01T00:00:00Z\",\"@l\":\"Error\",\"@m\":\"Payment failed\",\"SourceContext\":\"Billing.PaymentProcessor\"}",
            "{\"@t\":\"2026-01-01T00:00:01Z\",\"@l\":\"Error\",\"@m\":\"Payment failed again\",\"SourceContext\":\"Billing.PaymentProcessor\"}",
            "{\"@t\":\"2026-01-01T00:00:02Z\",\"@l\":\"Information\",\"@m\":\"Payment ok\",\"SourceContext\":\"Billing.PaymentProcessor\"}",
            string.Empty,
        ]));

        var tools = new LogPatternTools(new FilePatternFrequencyAnalyzer());

        var result = await tools.TopErrorSources(fixture.FilePath, callSiteProperty: null, minLevel: null, topN: 10, CancellationToken.None);

        var entry = Assert.Single(result);
        Assert.Equal("Billing.PaymentProcessor", entry.PropertyValue);
        Assert.Equal(2, entry.Count);
    }

    [Fact]
    public async Task TopErrorSources_FallsBackToExceptionFrame_WhenCallSitePropertyAbsent()
    {
        using var fixture = new TempFileFixture();
        var exceptionText = "System.Exception: boom\n   at Billing.PaymentProcessor.Charge()";
        var line = $"{{\"@t\":\"2026-01-01T00:00:00Z\",\"@l\":\"Error\",\"@m\":\"Payment failed\",\"@x\":{JsonSerializer.Serialize(exceptionText)}}}";
        fixture.WriteAllText(line + "\n");

        var tools = new LogPatternTools(new FilePatternFrequencyAnalyzer());

        var result = await tools.TopErrorSources(fixture.FilePath, callSiteProperty: null, minLevel: null, topN: 10, CancellationToken.None);

        var entry = Assert.Single(result);
        Assert.Equal("Billing.PaymentProcessor.Charge", entry.PropertyValue);
    }

    [Fact]
    public async Task TopPatterns_TruncatesLongSampleMessages()
    {
        using var fixture = new TempFileFixture();
        var longMessage = new string('x', 5000);
        fixture.WriteAllText($"{{\"@t\":\"2026-01-01T00:00:00Z\",\"@m\":{JsonSerializer.Serialize(longMessage)}}}\n");

        var tools = new LogPatternTools(new FilePatternFrequencyAnalyzer());

        var result = await tools.TopPatterns(fixture.FilePath, minLevel: null, topN: 10, CancellationToken.None);

        var entry = Assert.Single(result);
        Assert.True(entry.SampleMessage.Length <= ResponseLimits.DefaultHardMaxTextLength + "…(truncated)".Length);
        Assert.EndsWith("(truncated)", entry.SampleMessage);
    }

    [Fact]
    public async Task TopPropertyValues_RanksByCountWithoutExceptionFallback()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            "{\"@t\":\"2026-01-01T00:00:00Z\",\"@m\":\"a\",\"UserId\":\"1\"}",
            "{\"@t\":\"2026-01-01T00:00:01Z\",\"@m\":\"b\",\"UserId\":\"1\"}",
            "{\"@t\":\"2026-01-01T00:00:02Z\",\"@m\":\"c\",\"UserId\":\"2\"}",
            string.Empty,
        ]));

        var tools = new LogPatternTools(new FilePatternFrequencyAnalyzer());

        var result = await tools.TopPropertyValues(fixture.FilePath, "UserId", minLevel: null, topN: 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("1", result[0].PropertyValue);
        Assert.Equal(2, result[0].Count);
    }
}
