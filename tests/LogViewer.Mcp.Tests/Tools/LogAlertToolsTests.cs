using LogViewer.Core.Configuration;
using LogViewer.Core.Documents;
using LogViewer.Core.Highlighting;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogAlertToolsTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    public LogAlertToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    private sealed class FakeCatalog(IReadOnlyList<OpenDocumentInfo> documents) : IOpenDocumentCatalog
    {
        public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => documents;
    }

    private static OpenDocumentInfo Document(string title, params AlertRecord[] alerts) =>
        new($@"C:\logs\{title}", null, title, TailSourceKind.File, false, false, RecentAlerts: alerts);

    [Fact]
    public void GetAlerts_ListsEachDocumentsAlertsNewestFirst_SkippingQuietDocuments()
    {
        var tools = new LogAlertTools(new FakeCatalog(
        [
            Document("api.log",
                new AlertRecord(Start, AlertKind.Threshold, "Errors", 10, "ERROR a"),
                new AlertRecord(Start.AddMinutes(5), AlertKind.NewPattern, null, 42, "ERROR new shape")),
            Document("quiet.log"),
        ]));

        var result = tools.GetAlerts(since: null, maxResults: 10);

        var doc = Assert.Single(result);
        Assert.Equal("api.log", doc.Title);
        Assert.Equal(["NewPattern", "Threshold"], doc.Alerts.Select(a => a.Kind));
        Assert.Equal("Errors", doc.Alerts[1].RuleName);
        Assert.Equal(42, doc.Alerts[0].LineNumber);
        Assert.False(doc.Truncated);
    }

    [Fact]
    public void GetAlerts_FiltersBySince_AndCapsPerDocument()
    {
        var alerts = Enumerable.Range(0, 10)
            .Select(i => new AlertRecord(Start.AddMinutes(i), AlertKind.Threshold, "Errors", i, $"line {i}"))
            .ToArray();
        var tools = new LogAlertTools(new FakeCatalog([Document("api.log", alerts)]));

        var result = tools.GetAlerts(since: "2026-09-23T10:04:00Z", maxResults: 3);

        var doc = Assert.Single(result);
        Assert.Equal([9L, 8, 7], doc.Alerts.Select(a => a.LineNumber));
        Assert.True(doc.Truncated);
    }

    [Fact]
    public void GetAlerts_RejectsAnInvalidSince()
    {
        var tools = new LogAlertTools(new FakeCatalog([]));

        Assert.Throws<ArgumentException>(() => tools.GetAlerts(since: "yesterday-ish", maxResults: 10));
    }
}
