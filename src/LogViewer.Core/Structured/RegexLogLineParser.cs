using System.Globalization;
using System.Text.RegularExpressions;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Structured;

/// <summary>Parses lines with a user-defined <see cref="CustomLogFormat"/> — see that type for the group-name
/// contract. Lines the pattern doesn't match return false and render as plain text.</summary>
public sealed class RegexLogLineParser : ILogLineParser
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly string[] TimestampGroups = ["timestamp", "ts", "time", "datetime"];
    private static readonly string[] LevelGroups = ["level", "lvl", "severity", "loglevel"];
    private static readonly string[] MessageGroups = ["message", "msg"];
    private static readonly string[] ExceptionGroups = ["exception", "error", "stacktrace"];
    private static readonly string[] TemplateGroups = ["template", "messagetemplate"];

    private readonly Regex _regex;
    private readonly string[] _groupNames;
    private readonly string[] _timestampFormats;
    private readonly DateTimeStyles _timestampStyles;

    private RegexLogLineParser(CustomLogFormat format, Regex regex)
    {
        FormatId = format.Id;
        DisplayName = string.IsNullOrWhiteSpace(format.Name) ? format.Id : format.Name;
        _regex = regex;
        _groupNames = regex.GetGroupNames().Where(n => !int.TryParse(n, out _)).ToArray();
        _timestampFormats = string.IsNullOrWhiteSpace(format.TimestampFormat)
            ? []
            : format.TimestampFormat.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _timestampStyles = DateTimeStyles.AllowWhiteSpaces | (format.TimestampIsUtc ? DateTimeStyles.AssumeUniversal : DateTimeStyles.AssumeLocal);
    }

    public string FormatId { get; }

    public string DisplayName { get; }

    /// <summary>Builds a parser, or returns false with a user-readable <paramref name="error"/> when the pattern is
    /// empty, doesn't compile, or has no named groups (it would then carry nothing beyond the raw line).</summary>
    public static bool TryCreate(CustomLogFormat format, out RegexLogLineParser? parser, out string? error)
    {
        parser = null;
        if (string.IsNullOrWhiteSpace(format.Pattern))
        {
            error = "The pattern is empty.";
            return false;
        }

        Regex regex;
        try
        {
            var options = RegexOptions.CultureInvariant | (format.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            regex = new Regex(format.Pattern, options, MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        if (!regex.GetGroupNames().Any(n => !int.TryParse(n, out _)))
        {
            error = "The pattern has no named groups — use (?<level>…), (?<message>…), (?<timestamp>…) etc.";
            return false;
        }

        parser = new RegexLogLineParser(format, regex);
        error = null;
        return true;
    }

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        evt = null;

        Match match;
        try
        {
            match = _regex.Match(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (!match.Success)
        {
            return false;
        }

        string? timestampText = null, date = null, time = null, level = null, message = null, exception = null;
        string? traceId = null, spanId = null, template = null;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var name in _groupNames)
        {
            var group = match.Groups[name];
            if (!group.Success)
            {
                continue;
            }

            var value = group.Value;
            if (Is(name, TimestampGroups) && name.Equals("time", StringComparison.OrdinalIgnoreCase)) { time = value; }
            else if (Is(name, TimestampGroups)) { timestampText ??= value; }
            else if (name.Equals("date", StringComparison.OrdinalIgnoreCase)) { date = value; }
            else if (Is(name, LevelGroups)) { level ??= value; }
            else if (Is(name, MessageGroups)) { message ??= value; }
            else if (Is(name, ExceptionGroups)) { exception ??= value; }
            else if (Is(name, TemplateGroups)) { template ??= value; }
            else if (name.Equals("traceid", StringComparison.OrdinalIgnoreCase)) { traceId ??= value; }
            else if (name.Equals("spanid", StringComparison.OrdinalIgnoreCase)) { spanId ??= value; }
            else if (value.Length > 0) { properties[name] = value; }
        }

        timestampText ??= date is not null && time is not null ? $"{date} {time}" : date ?? time;
        var timestamp = timestampText is null ? null : ParseTimestamp(timestampText.Trim());

        evt = new StructuredLogEvent(
            timestamp,
            level is null ? null : LogLevelNormalizer.Normalize(level),
            template,
            message ?? line,
            string.IsNullOrEmpty(exception) ? null : exception,
            properties,
            string.IsNullOrEmpty(traceId) ? null : traceId,
            string.IsNullOrEmpty(spanId) ? null : spanId);
        return true;
    }

    private DateTimeOffset? ParseTimestamp(string text)
    {
        if (_timestampFormats.Length > 0)
        {
            return DateTimeOffset.TryParseExact(text, _timestampFormats, CultureInfo.InvariantCulture, _timestampStyles, out var exact)
                ? exact
                : null;
        }

        // "2026-09-23 14:05:00,123" (log4j/Python) — the comma isn't a fraction separator .NET understands.
        var normalized = Regex.Replace(text, @"(?<=:\d{2}),(?=\d)", ".");
        if (DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, _timestampStyles, out var parsed))
        {
            return parsed;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch) && epoch > 1e9)
        {
            // Unix epoch seconds (or milliseconds when implausibly large for seconds).
            return epoch > 1e11
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)epoch)
                : DateTimeOffset.FromUnixTimeMilliseconds((long)(epoch * 1000));
        }

        return MergedTimestampExtractor.TryExtract(text);
    }

    private static bool Is(string name, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
