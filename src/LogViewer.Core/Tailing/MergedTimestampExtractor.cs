using System.Globalization;
using System.Text.RegularExpressions;

namespace LogViewer.Core.Tailing;

/// <summary>Best-effort extraction of a leading timestamp from a raw log line, for ordering a
/// <see cref="MergedTailSource"/>. Recognizes ISO-8601 (with or without a <c>T</c> separator, optional
/// fractional seconds and offset) and the common <c>yyyy-MM-dd HH:mm:ss,fff</c> / <c>HH:mm:ss</c> shapes.</summary>
public static partial class MergedTimestampExtractor
{
    [GeneratedRegex(@"(?<!\d)(?<date>\d{4}-\d{2}-\d{2})[T ](?<time>\d{2}:\d{2}:\d{2}(?:[.,]\d{1,9})?)(?<off>Z|[+-]\d{2}:?\d{2})?")]
    private static partial Regex IsoPattern();

    [GeneratedRegex(@"(?<!\d)(?<h>\d{2}):(?<m>\d{2}):(?<s>\d{2}(?:[.,]\d{1,9})?)(?!\d)")]
    private static partial Regex TimeOnlyPattern();

    public static DateTimeOffset? TryExtract(string line)
    {
        var iso = IsoPattern().Match(line);
        if (iso.Success)
        {
            var text = $"{iso.Groups["date"].Value}T{iso.Groups["time"].Value.Replace(',', '.')}{NormalizeOffset(iso.Groups["off"].Value)}";
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind | DateTimeStyles.AssumeUniversal, out var ts))
            {
                return ts;
            }
        }

        var timeOnly = TimeOnlyPattern().Match(line);
        if (timeOnly.Success
            && TimeSpan.TryParse($"{timeOnly.Groups["h"].Value}:{timeOnly.Groups["m"].Value}:{timeOnly.Groups["s"].Value.Replace(',', '.')}",
                CultureInfo.InvariantCulture, out var tod))
        {
            // No date component — anchor to a fixed epoch date so lines still sort against each other.
            return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero) + tod;
        }

        return null;
    }

    private static string NormalizeOffset(string raw) => raw switch
    {
        "" => string.Empty,
        "Z" => "Z",
        _ => raw.Contains(':') ? raw : $"{raw[..3]}:{raw[3..]}",
    };
}
