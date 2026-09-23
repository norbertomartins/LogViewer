using System.ComponentModel;
using System.Globalization;
using LogViewer.Core.Documents;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

public sealed record AlertDto(string RaisedAt, string Kind, string? RuleName, long LineNumber, string LineText);

public sealed record DocumentAlerts(string SourcePath, string Title, IReadOnlyList<AlertDto> Alerts, bool Truncated);

[McpServerToolType]
public sealed class LogAlertTools(IOpenDocumentCatalog documentCatalog)
{
    [McpServerTool(Name = "logs_get_alerts")]
    [Description(
        "Lists the alerts each open LogViewer document raised while tailing, newest first: 'Threshold' when a " +
        "highlight rule with an alert matched N times within its time window (ruleName says which), 'NewPattern' " +
        "when a Warning/Error message shape appeared for the first time. Each alert carries the line that triggered " +
        "it. Recorded even when desktop notifications are off; only the most recent 200 per document are kept.")]
    public IReadOnlyList<DocumentAlerts> GetAlerts(
        [Description("Only alerts raised at or after this time (ISO 8601, e.g. 2026-09-23T14:00:00Z); omit for all.")] string? since,
        [Description("Maximum alerts to return per document.")] int maxResults)
    {
        DateTimeOffset? from = null;
        if (!string.IsNullOrWhiteSpace(since))
        {
            if (!DateTimeOffset.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                throw new ArgumentException($"'since' is not a valid timestamp: '{since}'.", nameof(since));
            }

            from = parsed;
        }

        var cap = ResponseLimits.ClampRows(maxResults);
        var result = new List<DocumentAlerts>();
        foreach (var document in documentCatalog.GetOpenDocuments())
        {
            var alerts = (document.RecentAlerts ?? [])
                .Where(a => from is null || a.RaisedAt >= from)
                .Reverse()
                .ToList();
            if (alerts.Count == 0)
            {
                continue;
            }

            var dtos = alerts.Take(cap)
                .Select(a => new AlertDto(
                    a.RaisedAt.ToString("O", CultureInfo.InvariantCulture), a.Kind.ToString(), a.RuleName, a.LineNumber, ResponseLimits.Truncate(a.LineText)))
                .ToList();
            result.Add(new DocumentAlerts(document.SearchableFilePath ?? document.SourcePath, document.Title, dtos, alerts.Count > cap));
        }

        return result;
    }
}
