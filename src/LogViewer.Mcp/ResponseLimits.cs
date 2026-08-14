namespace LogViewer.Mcp;

/// <summary>Hard caps applied by every tool regardless of what the calling agent requests, so a
/// misbehaving or overly broad request can't blow up the response payload or pin the CPU scanning a
/// huge file. <see cref="Configure"/> lets the configured <see cref="Core.Configuration.McpServerSettings"/>
/// tighten (never loosen) these defaults.</summary>
public static class ResponseLimits
{
    public const int DefaultHardMaxRows = 500;
    public const int DefaultHardMaxTextLength = 4000;

    private static int _hardMaxRows = DefaultHardMaxRows;
    private static int _hardMaxTextLength = DefaultHardMaxTextLength;

    public static void Configure(int maxResultsPerCall, int maxLineTextLength)
    {
        _hardMaxRows = maxResultsPerCall > 0 ? Math.Min(maxResultsPerCall, DefaultHardMaxRows) : DefaultHardMaxRows;
        _hardMaxTextLength = maxLineTextLength > 0 ? Math.Min(maxLineTextLength, DefaultHardMaxTextLength) : DefaultHardMaxTextLength;
    }

    /// <summary>Clamps a caller-requested row count to at least 1 and at most the configured hard cap.</summary>
    public static int ClampRows(int requested) => Math.Clamp(requested <= 0 ? _hardMaxRows : requested, 1, _hardMaxRows);

    /// <summary>Truncates a line/message of text to the configured hard cap, appending a marker so the
    /// agent knows the text was cut rather than mistaking it for the whole line.</summary>
    public static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= _hardMaxTextLength ? text : text[.._hardMaxTextLength] + "…(truncated)";
    }
}
