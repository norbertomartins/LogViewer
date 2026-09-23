using System.ComponentModel;
using LogViewer.Core.Analysis;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

[McpServerToolType]
public sealed class LogExceptionTools
{
    private const int MaxLineNumbersPerGroup = 20;

    [McpServerTool(Name = "logs_exception_groups")]
    [Description(
        "Finds every exception/stack trace in a log file (structured events with an exception field, plain-text " +
        ".NET/Java traces, Python tracebacks) and groups identical ones by exception type + top stack frames, " +
        "ignoring line numbers and variable message parts. Returns one row per distinct failure with its count, " +
        "first/last occurrence and a sample trace — the tool to reach for when asked which errors keep happening " +
        "or what the distinct crashes in a log are. Works on plain-text logs too, unlike logs_top_error_sources.")]
    public async Task<IReadOnlyList<ExceptionGroup>> ExceptionGroups(
        [Description("Full path to the log file (any format).")] string sourcePath,
        [Description("Maximum number of groups to return, most frequent first.")] int topN,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(topN);
        var groups = await ExceptionGrouper.GroupFileAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        return groups
            .Take(cap)
            .Select(g => g with
            {
                SampleMessage = ResponseLimits.Truncate(g.SampleMessage),
                TopFrame = g.TopFrame is null ? null : ResponseLimits.Truncate(g.TopFrame),
                SampleText = ResponseLimits.Truncate(g.SampleText),
                LineNumbers = [.. g.LineNumbers.Take(MaxLineNumbersPerGroup)],
            })
            .ToList();
    }
}
