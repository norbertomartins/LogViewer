using LogViewer.Core.Analysis;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Analysis;

public sealed class FilePatternFrequencyAnalyzerTests
{
    [Fact]
    public async Task AnalyzeBySignatureAsync_GroupsRepeatedMessageShapes_OrderedByCountDescending()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "Login attempt for {Username}", "alice"),
            Clef("2026-01-01T00:00:01Z", "Information", "Login attempt for {Username}", "bob"),
            Clef("2026-01-01T00:00:02Z", "Information", "Login attempt for {Username}", "carol"),
            Clef("2026-01-01T00:00:03Z", "Warning", "Slow query detected", null),
            string.Empty,
        ]));

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeBySignatureAsync(fixture.FilePath, minLevel: null, topN: 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Count);
        Assert.Equal(1, result[0].FirstLineNumber);
        Assert.Equal(3, result[0].LastLineNumber);
        Assert.Equal(1, result[1].Count);
    }

    [Fact]
    public async Task AnalyzeBySignatureAsync_MinLevelFiltersOutLowerSeverityLines()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "Request handled", null),
            Clef("2026-01-01T00:00:01Z", "Error", "Request failed", null),
            Clef("2026-01-01T00:00:02Z", "Fatal", "Process crashed", null),
            string.Empty,
        ]));

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeBySignatureAsync(fixture.FilePath, minLevel: "Error", topN: 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.True(e.Level is "Error" or "Fatal"));
    }

    [Fact]
    public async Task AnalyzeBySignatureAsync_TopNLimitsResultCount()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "Information", "Event A", null),
            Clef("2026-01-01T00:00:01Z", "Information", "Event B", null),
            Clef("2026-01-01T00:00:02Z", "Information", "Event C", null),
            string.Empty,
        ]));

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeBySignatureAsync(fixture.FilePath, minLevel: null, topN: 2, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task AnalyzeByPropertyAsync_GroupsByCallSiteProperty_RankedByErrorCount()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            ClefWithSource("2026-01-01T00:00:00Z", "Error", "Payment failed", "Billing.PaymentProcessor"),
            ClefWithSource("2026-01-01T00:00:01Z", "Error", "Payment retry failed", "Billing.PaymentProcessor"),
            ClefWithSource("2026-01-01T00:00:02Z", "Error", "Timeout", "Network.HttpClient"),
            ClefWithSource("2026-01-01T00:00:03Z", "Information", "Payment ok", "Billing.PaymentProcessor"),
            string.Empty,
        ]));

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeByPropertyAsync(
            fixture.FilePath, "SourceContext", minLevel: "Error", useExceptionFrameFallback: false, topN: 10, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Billing.PaymentProcessor", result[0].PropertyValue);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(2, result[0].DistinctSignatureCount);
    }

    [Fact]
    public async Task AnalyzeByPropertyAsync_ExceptionFrameFallback_UsedWhenPropertyAbsent()
    {
        using var fixture = new TempFileFixture();
        var exceptionText = "System.InvalidOperationException: boom\n   at Billing.PaymentProcessor.Charge(Decimal amount) in C:\\Billing.cs:line 42";
        var line = $"{{\"@t\":\"2026-01-01T00:00:00Z\",\"@l\":\"Error\",\"@m\":\"Payment failed\",\"@x\":{System.Text.Json.JsonSerializer.Serialize(exceptionText)}}}";

        fixture.WriteAllText(line + "\n");

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeByPropertyAsync(
            fixture.FilePath, "SourceContext", minLevel: "Error", useExceptionFrameFallback: true, topN: 10, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Billing.PaymentProcessor.Charge", result[0].PropertyValue);
    }

    [Fact]
    public async Task AnalyzeByPropertyAsync_WithoutFallback_SkipsEventsMissingProperty()
    {
        using var fixture = new TempFileFixture();
        var exceptionText = "System.Exception: boom\n   at Billing.PaymentProcessor.Charge()";
        var line = $"{{\"@t\":\"2026-01-01T00:00:00Z\",\"@l\":\"Error\",\"@m\":\"Payment failed\",\"@x\":{System.Text.Json.JsonSerializer.Serialize(exceptionText)}}}";

        fixture.WriteAllText(line + "\n");

        var analyzer = new FilePatternFrequencyAnalyzer();
        var result = await analyzer.AnalyzeByPropertyAsync(
            fixture.FilePath, "SourceContext", minLevel: "Error", useExceptionFrameFallback: false, topN: 10, CancellationToken.None);

        Assert.Empty(result);
    }

    private static string Clef(string timestamp, string level, string messageTemplateOrText, string? username)
    {
        var usernameJson = username is null ? string.Empty : $",\"Username\":\"{username}\"";
        return messageTemplateOrText.Contains('{')
            ? $"{{\"@t\":\"{timestamp}\",\"@l\":\"{level}\",\"@mt\":\"{messageTemplateOrText}\"{usernameJson}}}"
            : $"{{\"@t\":\"{timestamp}\",\"@l\":\"{level}\",\"@m\":\"{messageTemplateOrText}\"{usernameJson}}}";
    }

    private static string ClefWithSource(string timestamp, string level, string message, string sourceContext) =>
        $"{{\"@t\":\"{timestamp}\",\"@l\":\"{level}\",\"@m\":\"{message}\",\"SourceContext\":\"{sourceContext}\"}}";
}
