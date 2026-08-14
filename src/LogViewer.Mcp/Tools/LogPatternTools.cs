using System.ComponentModel;
using LogViewer.Core.Analysis;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

[McpServerToolType]
public sealed class LogPatternTools(IPatternFrequencyAnalyzer patternFrequencyAnalyzer)
{
    [McpServerTool(Name = "logs_top_patterns")]
    [Description(
        "Finds the most frequently recurring log message shapes in a structured log file, collapsing occurrences " +
        "of the same log statement regardless of its dynamic values (ids, durations, timestamps, ...). Use this " +
        "to discover general recurring patterns; use logs_top_error_sources for error-focused ranking by call site.")]
    public async Task<IReadOnlyList<PatternFrequencyEntry>> TopPatterns(
        [Description("Full path to the structured (Serilog JSON) log file.")] string sourcePath,
        [Description("Optional minimum level to include (Verbose/Debug/Information/Warning/Error/Fatal). Null includes all levels.")]
        string? minLevel,
        [Description("Maximum number of pattern rows to return, ranked by descending count.")] int topN,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(topN);
        var result = await patternFrequencyAnalyzer.AnalyzeBySignatureAsync(sourcePath, minLevel, cap, cancellationToken).ConfigureAwait(false);
        return result.Select(e => e with { SampleMessage = ResponseLimits.Truncate(e.SampleMessage) }).ToList();
    }

    [McpServerTool(Name = "logs_top_error_sources")]
    [Description(
        "Ranks functions/classes/modules by how many Error-level-or-above log lines they produced, using the " +
        "callSiteProperty structured field (SourceContext by default) or, when that's absent on an event that " +
        "carries an exception, the topmost stack trace frame instead. This is the tool to reach for when asked " +
        "which functions/call-sites are repeatedly logging errors.")]
    public async Task<IReadOnlyList<PropertyFrequencyEntry>> TopErrorSources(
        [Description("Full path to the structured (Serilog JSON) log file.")] string sourcePath,
        [Description("Structured property identifying the call site (default SourceContext).")] string? callSiteProperty,
        [Description("Minimum level to include (default Error).")] string? minLevel,
        [Description("Maximum number of rows to return, ranked by descending error count.")] int topN,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(topN);
        var property = string.IsNullOrWhiteSpace(callSiteProperty) ? "SourceContext" : callSiteProperty;
        var level = string.IsNullOrWhiteSpace(minLevel) ? "Error" : minLevel;

        var result = await patternFrequencyAnalyzer.AnalyzeByPropertyAsync(
            sourcePath, property, level, useExceptionFrameFallback: true, cap, cancellationToken).ConfigureAwait(false);
        return result.Select(e => e with { PropertyValue = ResponseLimits.Truncate(e.PropertyValue) }).ToList();
    }

    [McpServerTool(Name = "logs_top_property_values")]
    [Description(
        "Ranks the most frequent values of any structured log property (e.g. RequestId, UserId, StatusCode) — " +
        "the general-purpose sibling of logs_top_error_sources for non-error-focused property analysis.")]
    public async Task<IReadOnlyList<PropertyFrequencyEntry>> TopPropertyValues(
        [Description("Full path to the structured (Serilog JSON) log file.")] string sourcePath,
        [Description("Structured property name to rank values of.")] string propertyName,
        [Description("Optional minimum level to include. Null includes all levels.")] string? minLevel,
        [Description("Maximum number of rows to return, ranked by descending count.")] int topN,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(topN);
        var result = await patternFrequencyAnalyzer.AnalyzeByPropertyAsync(
            sourcePath, propertyName, minLevel, useExceptionFrameFallback: false, cap, cancellationToken).ConfigureAwait(false);
        return result.Select(e => e with { PropertyValue = ResponseLimits.Truncate(e.PropertyValue) }).ToList();
    }
}
