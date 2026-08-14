using System.ComponentModel;
using LogViewer.Core.Analysis;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Search;
using LogViewer.Core.Structured;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

[McpServerToolType]
public sealed class LogSearchTools(IFullTextSearchService searchService, ILineWindowReader lineWindowReader)
{
    private const int MaxContextLinesPerSide = 100;

    [McpServerTool(Name = "logs_search")]
    [Description(
        "Searches a log file for lines matching a plain-text or regex pattern, optionally restricted to a " +
        "specific structured property (e.g. @Message, @Exception, or a named field) instead of the raw line. " +
        "Results are capped; check the truncated flag if you may need to narrow the pattern.")]
    public async Task<SearchToolResult> Search(
        [Description("Full path to the log file to search.")] string sourcePath,
        [Description("Text or regex pattern to match.")] string pattern,
        [Description("Treat pattern as a regular expression.")] bool isRegex,
        [Description("Case-sensitive match.")] bool isCaseSensitive,
        [Description("Optional structured field to match against instead of the raw line (@Level, @Message, @Exception, or a property name).")]
        string? propertyName,
        [Description("Maximum number of results to return.")] int maxResults,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(maxResults);
        var results = new List<SearchResultDto>(cap);
        var truncated = false;

        await foreach (var match in searchService.SearchAsync(sourcePath, pattern, isRegex, isCaseSensitive, propertyName, cancellationToken).ConfigureAwait(false))
        {
            if (results.Count >= cap)
            {
                truncated = true;
                break;
            }

            results.Add(new SearchResultDto(match.LineNumber, ResponseLimits.Truncate(match.Text)));
        }

        return new SearchToolResult(results, truncated);
    }

    [McpServerTool(Name = "logs_get_line_context")]
    [Description(
        "Reads a window of raw lines around a specific line number in a log file, for inspecting the full " +
        "context of a match found via logs_search, logs_top_patterns, or logs_top_error_sources.")]
    public async Task<LineContextResult> GetLineContext(
        [Description("Full path to the log file.")] string sourcePath,
        [Description("The line number to center the window on (1-based).")] long lineNumber,
        [Description("Number of lines to include before the center line.")] int linesBefore,
        [Description("Number of lines to include after the center line.")] int linesAfter,
        CancellationToken cancellationToken)
    {
        var before = Math.Clamp(linesBefore, 0, MaxContextLinesPerSide);
        var after = Math.Clamp(linesAfter, 0, MaxContextLinesPerSide);

        var window = await lineWindowReader.ReadAsync(sourcePath, lineNumber, before, after, cancellationToken).ConfigureAwait(false);

        return new LineContextResult(
            window.RequestedLineNumber,
            window.LineNumberOutOfRange,
            window.Lines.Select(l => new SearchResultDto(l.LineNumber, ResponseLimits.Truncate(l.Text))).ToList());
    }

    [McpServerTool(Name = "logs_pattern_occurrences")]
    [Description(
        "Finds concrete occurrences of a message pattern signature (as returned by logs_top_patterns) in a " +
        "structured log file — a drill-down from a pattern's summary row to its individual lines.")]
    public async Task<SearchToolResult> PatternOccurrences(
        [Description("Full path to the structured (Serilog JSON) log file.")] string sourcePath,
        [Description("The pattern signature to match, as returned by logs_top_patterns.")] string signature,
        [Description("Maximum number of results to return.")] int maxResults,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(maxResults);
        var results = new List<SearchResultDto>(cap);
        var truncated = false;

        await foreach (var (lineNumber, evt) in StructuredFileReader.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(MessageSignature.Compute(evt), signature, StringComparison.Ordinal))
            {
                continue;
            }

            if (results.Count >= cap)
            {
                truncated = true;
                break;
            }

            results.Add(new SearchResultDto(lineNumber, ResponseLimits.Truncate(evt.RenderedMessage)));
        }

        return new SearchToolResult(results, truncated);
    }
}
