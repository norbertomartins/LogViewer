using System.Globalization;
using System.Text.RegularExpressions;

namespace LogViewer.Core.Analysis;

/// <summary>
/// Parses what a user types into a "go to time" / time-range box into an absolute <see cref="DateTimeOffset"/>.
/// Accepted forms:
/// <list type="bullet">
/// <item>a full date-time — <c>2026-09-23 14:05</c>, <c>2026-09-23T14:05:30.250</c>, <c>2026-09-23 14:05:30+01:00</c>;</item>
/// <item>a time of day — <c>14:05</c>, <c>14:05:30</c>, <c>14:05:30,123</c> — taken on the reference's date;</item>
/// <item>a relative offset from the reference — <c>-5m</c>, <c>+30s</c>, <c>-2h</c>, <c>-1d</c>, <c>-1h30m</c>, <c>-250ms</c>.</item>
/// </list>
/// Input without an explicit offset is read as wall-clock time in the <b>reference's</b> offset — i.e. the time
/// as written in the log itself (the reference is normally the newest line's timestamp), not the machine's
/// local zone, since that's what the user sees when reading the raw lines.
/// </summary>
public static partial class TimestampQuery
{
    [GeneratedRegex(@"^(?<sign>[+-])\s*(?<parts>(?:\d+(?:\.\d+)?\s*(?:ms|d|h|m|s)\s*)+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RelativePattern();

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|d|h|m|s)", RegexOptions.IgnoreCase)]
    private static partial Regex RelativePartPattern();

    [GeneratedRegex(@"^(?<h>\d{1,2}):(?<m>\d{2})(?::(?<s>\d{2}(?:[.,]\d{1,7})?))?$")]
    private static partial Regex TimeOfDayPattern();

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF", "yyyy-MM-dd HH:mm:ss,FFFFFFF",
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.FFFFFFF", "yyyy-MM-ddTHH:mm:ss,FFFFFFF",
        "yyyy-MM-dd",
    ];

    public static bool TryParse(string? input, DateTimeOffset? reference, out DateTimeOffset result)
    {
        result = default;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var offset = reference?.Offset ?? TimeSpan.Zero;

        var relative = RelativePattern().Match(text);
        if (relative.Success)
        {
            if (reference is null)
            {
                return false;
            }

            var delta = TimeSpan.Zero;
            foreach (Match part in RelativePartPattern().Matches(relative.Groups["parts"].Value))
            {
                var value = double.Parse(part.Groups["value"].Value, CultureInfo.InvariantCulture);
                delta += part.Groups["unit"].Value.ToLowerInvariant() switch
                {
                    "ms" => TimeSpan.FromMilliseconds(value),
                    "s" => TimeSpan.FromSeconds(value),
                    "m" => TimeSpan.FromMinutes(value),
                    "h" => TimeSpan.FromHours(value),
                    _ => TimeSpan.FromDays(value),
                };
            }

            result = relative.Groups["sign"].Value == "-" ? reference.Value - delta : reference.Value + delta;
            return true;
        }

        var timeOfDay = TimeOfDayPattern().Match(text);
        if (timeOfDay.Success)
        {
            var seconds = timeOfDay.Groups["s"].Success
                ? double.Parse(timeOfDay.Groups["s"].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
                : 0;
            var hours = int.Parse(timeOfDay.Groups["h"].Value, CultureInfo.InvariantCulture);
            var minutes = int.Parse(timeOfDay.Groups["m"].Value, CultureInfo.InvariantCulture);
            if (hours > 23 || minutes > 59 || seconds >= 60)
            {
                return false;
            }

            var date = reference?.Date ?? DateTime.Today;
            result = new DateTimeOffset(date, offset) + new TimeSpan(hours, minutes, 0) + TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            result = new DateTimeOffset(local, offset);
            return true;
        }

        // Anything carrying its own offset/zone (ISO-8601 with Z or ±hh:mm) is taken as-is.
        if (HasExplicitOffset(text)
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var absolute))
        {
            result = absolute;
            return true;
        }

        return false;
    }

    private static bool HasExplicitOffset(string text) =>
        text.EndsWith('Z') || text.EndsWith('z') || Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
}
