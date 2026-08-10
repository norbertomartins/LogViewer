namespace LogViewer.Core.Structured;

/// <summary>Canonical ordering of Serilog level names/abbreviations, shared by the "Min Level" row filter and
/// <see cref="LogViewer.App.Converters.LevelToBrushConverter"/> (App layer) so both agree on the same ranks.</summary>
public static class LogLevelSeverity
{
    public static readonly IReadOnlyList<string> Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>Higher is more severe; null for an unrecognized or missing level name.</summary>
    public static int? Rank(string? level)
    {
        if (string.IsNullOrEmpty(level))
        {
            return null;
        }

        return level.ToUpperInvariant() switch
        {
            "VERBOSE" or "VRB" => 0,
            "DEBUG" or "DBG" => 1,
            "INFORMATION" or "INFO" or "INF" => 2,
            "WARNING" or "WARN" or "WRN" => 3,
            "ERROR" or "ERR" => 4,
            "FATAL" or "FTL" => 5,
            _ => null,
        };
    }
}
