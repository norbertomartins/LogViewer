using System.ComponentModel;
using LogViewer.Core.Analysis;
using LogViewer.Core.Documents;
using LogViewer.Core.Structured;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

[McpServerToolType]
public sealed class LogDiscoveryTools(IOpenDocumentCatalog documentCatalog, ILineWindowReader lineWindowReader)
{
    [McpServerTool(Name = "logs_list_open_documents")]
    [Description(
        "Lists the log documents currently open/tailed in the running LogViewer app, with their file paths, " +
        "titles, source kind, and whether each is the active tab. Call this first to discover what's available " +
        "without asking the user for a path.")]
    public IReadOnlyList<OpenDocumentSummary> ListOpenDocuments()
    {
        return documentCatalog.GetOpenDocuments()
            .Select(d => new OpenDocumentSummary(d.SourcePath, d.SearchableFilePath, d.Title, d.Kind.ToString(), d.IsActive, d.IsStructuredView))
            .ToList();
    }

    [McpServerTool(Name = "logs_describe_source")]
    [Description(
        "Inspects a log file on disk: whether it exists, its size, last-write time, whether it looks like " +
        "structured Serilog JSON output, and a small sample of its first lines.")]
    public async Task<DescribeSourceResult> DescribeSource(
        [Description("Full path to the log file.")] string sourcePath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return new DescribeSourceResult(false, null, null, false, []);
        }

        var info = new FileInfo(sourcePath);
        var window = await lineWindowReader.ReadAsync(sourcePath, centerLineNumber: 1, linesBefore: 0, linesAfter: 19, cancellationToken).ConfigureAwait(false);
        var sampleTexts = window.Lines.Select(l => l.Text).ToList();
        var looksStructured = SerilogFormatDetector.LooksLikeSerilogJson(sampleTexts);

        return new DescribeSourceResult(
            true, info.Length, info.LastWriteTimeUtc, looksStructured,
            sampleTexts.Take(5).Select(ResponseLimits.Truncate).ToList());
    }

    [McpServerTool(Name = "logs_list_structured_properties")]
    [Description(
        "Samples a structured (Serilog JSON) log file and lists the distinct property names seen, so an agent " +
        "can discover valid propertyName values for logs_top_property_values/logs_search before guessing.")]
    public async Task<IReadOnlyList<string>> ListStructuredProperties(
        [Description("Full path to the log file.")] string sourcePath,
        [Description("Number of lines to sample from the start of the file (default 2000, max 20000).")] int sampleSize,
        CancellationToken cancellationToken)
    {
        var effectiveSampleSize = Math.Clamp(sampleSize <= 0 ? 2000 : sampleSize, 1, 20_000);
        var properties = new SortedSet<string>(StringComparer.Ordinal);

        var count = 0;
        await foreach (var (_, evt) in StructuredFileReader.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        {
            foreach (var key in evt.Properties.Keys)
            {
                properties.Add(key);
            }

            count++;
            if (count >= effectiveSampleSize)
            {
                break;
            }
        }

        return properties.ToList();
    }
}
