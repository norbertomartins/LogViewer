using System.Globalization;
using System.Text;

namespace LogViewer.Core.Structured;

/// <summary>
/// Parses <c>logfmt</c> lines — a flat sequence of <c>key=value</c> pairs, values optionally
/// double-quoted (<c>msg="hello world"</c>), as emitted by Go's <c>log/slog</c>, Logrus, Heroku,
/// zap's console encoder, etc. Well-known keys (<c>level</c>/<c>ts</c>/<c>msg</c>/<c>error</c> and common
/// aliases) map onto <see cref="StructuredLogEvent"/>; everything else becomes a property.
/// </summary>
public sealed class LogfmtLogLineParser : ILogLineParser
{
    private static readonly string[] LevelKeys = ["level", "lvl", "severity", "loglevel"];
    private static readonly string[] MessageKeys = ["msg", "message"];
    private static readonly string[] TimestampKeys = ["ts", "time", "timestamp", "@timestamp", "t"];
    private static readonly string[] ExceptionKeys = ["error", "err", "exception", "stacktrace", "stack"];

    public string FormatId => "logfmt";

    public string DisplayName => "logfmt (key=value)";

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        evt = null;

        var pairs = Parse(line);
        if (pairs.Count < 2)
        {
            return false;
        }

        // Require at least a message or a level so we don't grab arbitrary "a=b c=d" text.
        string? level = null, message = null, exception = null;
        DateTimeOffset? timestamp = null;
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in pairs)
        {
            if (level is null && Matches(key, LevelKeys)) { level = LogLevelNormalizer.Normalize(value); continue; }
            if (message is null && Matches(key, MessageKeys)) { message = value; continue; }
            if (exception is null && Matches(key, ExceptionKeys)) { exception = value; continue; }
            if (timestamp is null && Matches(key, TimestampKeys) && TryReadTimestamp(value, out var ts)) { timestamp = ts; continue; }
            properties[key] = value;
        }

        if (message is null && level is null)
        {
            return false;
        }

        evt = new StructuredLogEvent(timestamp, level ?? "Information", null, message ?? string.Empty, exception, properties);
        return true;
    }

    private static bool Matches(string key, string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.Equals(key, c, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static List<KeyValuePair<string, string>> Parse(string line)
    {
        var result = new List<KeyValuePair<string, string>>();
        var i = 0;
        var n = line.Length;

        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            if (i >= n)
            {
                break;
            }

            var keyStart = i;
            while (i < n && line[i] != '=' && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            var key = line[keyStart..i];

            if (i >= n || line[i] != '=' || key.Length == 0)
            {
                // A bare token with no '=' — not logfmt structure; skip it.
                while (i < n && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                continue;
            }

            i++; // consume '='

            string value;
            if (i < n && line[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < n && line[i] != '"')
                {
                    if (line[i] == '\\' && i + 1 < n)
                    {
                        i++;
                        sb.Append(line[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', var c => c });
                    }
                    else
                    {
                        sb.Append(line[i]);
                    }

                    i++;
                }

                i++; // consume closing quote
                value = sb.ToString();
            }
            else
            {
                var valStart = i;
                while (i < n && !char.IsWhiteSpace(line[i]))
                {
                    i++;
                }

                value = line[valStart..i];
            }

            result.Add(new KeyValuePair<string, string>(key, value));
        }

        return result;
    }

    private static bool TryReadTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp))
        {
            return true;
        }

        if (long.TryParse(value, out var unix))
        {
            timestamp = value.Length >= 16
                ? DateTimeOffset.FromUnixTimeMilliseconds(unix / 1000)
                : value.Length >= 13
                    ? DateTimeOffset.FromUnixTimeMilliseconds(unix)
                    : DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }

        return false;
    }
}
