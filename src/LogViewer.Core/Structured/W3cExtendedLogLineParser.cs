using System.Globalization;

namespace LogViewer.Core.Structured;

/// <summary>
/// Parses the <b>W3C Extended Log File Format</b> used by IIS, Exchange, ISA/TMG and others. The column
/// layout is declared by a <c>#Fields:</c> directive that precedes the data rows, so this parser is
/// <b>stateful</b>: it remembers the most recent <c>#Fields:</c> line and splits subsequent space-delimited
/// rows against it. Directive lines (<c>#Software</c>, <c>#Version</c>, <c>#Date</c>, <c>#Fields</c>) are
/// consumed for state and reported as non-events (<c>TryParse</c> returns false).
/// <para><c>date</c>+<c>time</c> columns become the timestamp; <c>sc-status</c> drives the level
/// (5xx → Error, 4xx → Warning); method + stem + status form the message; every column is also a property.</para>
/// </summary>
public sealed class W3cExtendedLogLineParser : ILogLineParser
{
    private string[]? _fields;

    public string FormatId => "w3c";

    public string DisplayName => "W3C Extended / IIS";

    /// <summary>Pre-seeds the column layout (used by sample-based detection, which sees the header first).</summary>
    public void SetFields(IEnumerable<string> fields) => _fields = [.. fields];

    public bool TryParse(string line, out StructuredLogEvent? evt)
    {
        evt = null;

        if (line.StartsWith('#'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line.AsSpan(1, colon - 1).Trim().Equals("Fields", StringComparison.OrdinalIgnoreCase))
            {
                _fields = line[(colon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return false;
        }

        if (_fields is null || line.Length == 0)
        {
            return false;
        }

        var values = SplitRow(line);
        if (values.Count < _fields.Length)
        {
            return false;
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        string? date = null, time = null, method = null, stem = null, status = null, query = null, level = null, taken = null;

        for (var i = 0; i < _fields.Length; i++)
        {
            var key = _fields[i];
            var value = values[i] == "-" ? string.Empty : values[i];
            properties[key] = value;

            switch (key)
            {
                case "date": date = value; break;
                case "time": time = value; break;
                case "cs-method": method = value; break;
                case "cs-uri-stem": stem = value; break;
                case "cs-uri-query": query = value; break;
                case "sc-status": status = value; break;
                case "time-taken": taken = value; break;
                case "x-level" or "level": level = value; break;
            }
        }

        DateTimeOffset? timestamp = null;
        if (date is { Length: > 0 } && time is { Length: > 0 }
            && DateTimeOffset.TryParse($"{date}T{time}Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
        {
            timestamp = ts;
        }

        if (level is null && int.TryParse(status, out var statusCode))
        {
            level = LogLevelNormalizer.FromHttpStatus(statusCode);
        }

        var message = BuildMessage(method, stem, query, status, taken);

        evt = new StructuredLogEvent(timestamp, level ?? "Information", null, message, null, properties);
        return true;
    }

    private static string BuildMessage(string? method, string? stem, string? query, string? status, string? taken)
    {
        var parts = new List<string>(4);
        if (!string.IsNullOrEmpty(method))
        {
            parts.Add(method);
        }

        if (!string.IsNullOrEmpty(stem))
        {
            parts.Add(string.IsNullOrEmpty(query) ? stem : $"{stem}?{query}");
        }

        if (!string.IsNullOrEmpty(status))
        {
            parts.Add($"→ {status}");
        }

        if (!string.IsNullOrEmpty(taken))
        {
            parts.Add($"({taken} ms)");
        }

        return parts.Count > 0 ? string.Join(' ', parts) : string.Empty;
    }

    private static List<string> SplitRow(string line)
    {
        // W3C values are space-delimited; a quoted value may itself contain spaces.
        var result = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (line[i] == ' ')
            {
                i++;
                continue;
            }

            if (line[i] == '"')
            {
                var end = line.IndexOf('"', i + 1);
                if (end < 0)
                {
                    result.Add(line[(i + 1)..]);
                    break;
                }

                result.Add(line[(i + 1)..end]);
                i = end + 1;
            }
            else
            {
                var end = line.IndexOf(' ', i);
                if (end < 0)
                {
                    result.Add(line[i..]);
                    break;
                }

                result.Add(line[i..end]);
                i = end + 1;
            }
        }

        return result;
    }
}
