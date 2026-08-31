using System.Globalization;
using System.Text.Json;

namespace LogViewer.Core.Structured;

/// <summary>
/// Parses one-JSON-object-per-line logs that are <b>not</b> Serilog/CLEF — the shape emitted by
/// <c>Microsoft.Extensions.Logging</c>'s JSON console formatter, Bunyan, pino, Winston, zap's JSON
/// encoder, structured Docker/Kubernetes logs, etc. Recognizes common field-name aliases for
/// timestamp/level/message/exception and folds every other member into <see cref="StructuredLogEvent.Properties"/>.
/// <para><see cref="LogLineParsers"/> tries <see cref="SerilogLogLineParser"/> first, so CLEF lines never
/// reach this parser.</para>
/// </summary>
public sealed class GenericJsonLogLineParser : ILogLineParser
{
    private static readonly string[] TimestampKeys =
        ["timestamp", "ts", "time", "@timestamp", "Timestamp", "date", "eventTime", "asctime"];

    private static readonly string[] LevelKeys =
        ["level", "lvl", "severity", "loglevel", "LogLevel", "Level", "levelname", "SeverityText"];

    private static readonly string[] MessageKeys =
        ["message", "msg", "Message", "MessageTemplate", "text", "body", "log", "@message"];

    private static readonly string[] ExceptionKeys =
        ["exception", "error", "err", "Exception", "stack", "stack_trace", "stackTrace", "StackTrace"];

    public string FormatId => "ndjson";

    public string DisplayName => "JSON lines (NDJSON)";

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        evt = null;

        var trimmed = line.AsSpan().Trim();
        if (trimmed.IsEmpty || trimmed[0] != '{')
        {
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            string? level = null, message = null, exception = null;
            DateTimeOffset? timestamp = null;
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var member in root.EnumerateObject())
            {
                if (level is null && Matches(member.Name, LevelKeys))
                {
                    level = LogLevelNormalizer.Normalize(ScalarText(member.Value));
                    continue;
                }

                if (message is null && Matches(member.Name, MessageKeys))
                {
                    message = ScalarText(member.Value);
                    continue;
                }

                if (exception is null && Matches(member.Name, ExceptionKeys) && member.Value.ValueKind != JsonValueKind.Null)
                {
                    exception = ScalarText(member.Value);
                    continue;
                }

                if (timestamp is null && Matches(member.Name, TimestampKeys) && TryReadTimestamp(member.Value, out var ts))
                {
                    timestamp = ts;
                    continue;
                }

                properties[member.Name] = Flatten(member.Value);
            }

            if (message is null && level is null && properties.Count == 0)
            {
                return false;
            }

            evt = new StructuredLogEvent(timestamp, level ?? "Information", null, message ?? string.Empty, exception, properties);
            return true;
        }
    }

    private static bool Matches(string name, string[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => value.GetRawText(),
    };

    private static string Flatten(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    private static bool TryReadTimestamp(JsonElement value, out DateTimeOffset timestamp)
    {
        timestamp = default;

        if (value.ValueKind == JsonValueKind.String)
        {
            return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var unix))
        {
            // Heuristic: >1e14 → microseconds, >1e11 → milliseconds, else seconds.
            timestamp = unix switch
            {
                > 100_000_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(unix / 1000),
                > 100_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds(unix),
                _ => DateTimeOffset.FromUnixTimeSeconds(unix),
            };
            return true;
        }

        return false;
    }
}
