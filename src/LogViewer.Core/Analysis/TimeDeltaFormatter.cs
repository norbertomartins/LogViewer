using System.Globalization;

namespace LogViewer.Core.Analysis;

/// <summary>Compact signed rendering of the time between two log lines for the "Δ" column —
/// <c>+0ms</c>, <c>+12ms</c>, <c>+1.234s</c>, <c>+2m03s</c>, <c>+1h02m</c>, <c>+2d03h</c>.</summary>
public static class TimeDeltaFormatter
{
    public static string Format(TimeSpan delta)
    {
        var sign = delta < TimeSpan.Zero ? "-" : "+";
        var abs = delta.Duration();

        if (abs < TimeSpan.FromSeconds(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{(int)abs.TotalMilliseconds}ms");
        }

        if (abs < TimeSpan.FromMinutes(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.TotalSeconds:0.000}s");
        }

        if (abs < TimeSpan.FromHours(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Minutes}m{abs.Seconds:00}s");
        }

        if (abs < TimeSpan.FromDays(1))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs.Hours}h{abs.Minutes:00}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{sign}{(int)abs.TotalDays}d{abs.Hours:00}h");
    }
}
