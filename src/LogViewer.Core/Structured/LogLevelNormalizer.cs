namespace LogViewer.Core.Structured;

/// <summary>Maps the many level spellings found across log formats (logfmt, NDJSON, syslog severities,
/// HTTP status buckets) onto the canonical Serilog names <see cref="LogLevelSeverity"/> already ranks.</summary>
public static class LogLevelNormalizer
{
    /// <summary>Normalizes a textual level name; unknown values are title-cased and passed through.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Information";
        }

        return raw.Trim().ToUpperInvariant() switch
        {
            "TRACE" or "TRC" or "VERBOSE" or "VRB" => "Verbose",
            "DEBUG" or "DBG" or "FINE" => "Debug",
            "INFO" or "INFORMATION" or "INF" or "NOTICE" => "Information",
            "WARN" or "WARNING" or "WRN" => "Warning",
            "ERROR" or "ERR" or "SEVERE" or "CRIT" or "CRITICAL" => "Error",
            "FATAL" or "FTL" or "PANIC" or "EMERG" or "EMERGENCY" or "ALERT" => "Fatal",
            _ => raw.Length > 0 ? char.ToUpperInvariant(raw.Trim()[0]) + raw.Trim()[1..].ToLowerInvariant() : "Information",
        };
    }

    private static readonly System.Text.RegularExpressions.Regex LevelWordPattern = new(
        @"\b(TRACE|VERBOSE|VRB|DEBUG|DBG|INFO(?:RMATION)?|INF|NOTICE|WARN(?:ING)?|WRN|ERROR|ERR|FATAL|FTL|CRIT(?:ICAL)?|PANIC)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Best-effort severity rank (see <see cref="LogLevelSeverity.Rank"/>) for a raw, unstructured
    /// log line — scans for a level word and returns the highest severity found, or null if none.</summary>
    public static int? GuessSeverityFromLine(string line)
    {
        int? best = null;
        foreach (System.Text.RegularExpressions.Match m in LevelWordPattern.Matches(line))
        {
            if (LogLevelSeverity.Rank(Normalize(m.Value)) is { } rank && (best is null || rank > best))
            {
                best = rank;
            }
        }

        return best;
    }

    /// <summary>Maps an RFC 5424 numeric severity (0 = Emergency … 7 = Debug) onto a canonical level name.</summary>
    public static string FromSyslogSeverity(int severity) => severity switch
    {
        0 or 1 or 2 => "Fatal",
        3 => "Error",
        4 => "Warning",
        5 or 6 => "Information",
        _ => "Debug",
    };

    /// <summary>Maps an HTTP status code onto a level: 5xx → Error, 4xx → Warning, otherwise Information.</summary>
    public static string FromHttpStatus(int status) => status switch
    {
        >= 500 => "Error",
        >= 400 => "Warning",
        _ => "Information",
    };
}
