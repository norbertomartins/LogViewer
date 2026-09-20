using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LogViewer.Core.Structured;

/// <summary>
/// Parses a single line of Serilog JSON output — either the compact CLEF shape
/// (<c>Serilog.Formatting.Compact.CompactJsonFormatter</c>: "@t"/"@mt"/"@l"/"@x" plus top-level
/// properties) or the standard shape (<c>Serilog.Formatting.Json.JsonFormatter</c>: "Timestamp"/
/// "MessageTemplate"/"Level"/"Exception"/a nested "Properties" object). Both formatters write one
/// JSON object per line, so this slots directly into the existing line-based tailing pipeline.
/// </summary>
public static class SerilogEventParser
{
    private static readonly Regex TemplateTokenPattern = new(@"\{(@|\$)?([0-9A-Za-z_]+)(:[^}]*)?\}", RegexOptions.Compiled);

    public static bool TryParse(string line, out StructuredLogEvent? evt)
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

            evt = root.TryGetProperty("@t", out _) || root.TryGetProperty("@mt", out _) || root.TryGetProperty("@m", out _)
                ? ParseClef(root)
                : ParseStandard(root);
            return evt is not null;
        }
    }

    private static StructuredLogEvent ParseClef(JsonElement root)
    {
        var timestamp = ReadTimestamp(root, "@t");
        var level = root.TryGetProperty("@l", out var levelProp) ? levelProp.GetString() : "Information";
        var exception = root.TryGetProperty("@x", out var exProp) ? exProp.GetString() : null;

        var properties = new Dictionary<string, string>();
        foreach (var member in root.EnumerateObject())
        {
            if (member.Name.Length > 0 && member.Name[0] == '@')
            {
                continue;
            }

            properties[member.Name] = FlattenValue(member.Value);
        }

        string? template = null;
        string rendered;
        if (root.TryGetProperty("@mt", out var mtProp))
        {
            template = mtProp.GetString();
            rendered = RenderTemplate(template, properties);
        }
        else if (root.TryGetProperty("@m", out var mProp))
        {
            rendered = mProp.GetString() ?? string.Empty;
        }
        else
        {
            rendered = string.Empty;
        }

        var traceId = root.TryGetProperty("@tr", out var trProp) ? trProp.GetString() : null;
        var spanId = root.TryGetProperty("@sp", out var spProp) ? spProp.GetString() : null;
        PromoteTraceFields(properties, ref traceId, ref spanId);

        return new StructuredLogEvent(timestamp, level, template, rendered, exception, properties, traceId, spanId);
    }

    private static StructuredLogEvent? ParseStandard(JsonElement root)
    {
        if (!root.TryGetProperty("MessageTemplate", out var templateProp) && !root.TryGetProperty("RenderedMessage", out _))
        {
            return null;
        }

        var timestamp = ReadTimestamp(root, "Timestamp");
        var level = root.TryGetProperty("Level", out var levelProp) ? levelProp.GetString() : "Information";
        var exception = root.TryGetProperty("Exception", out var exProp) ? exProp.GetString() : null;

        var properties = new Dictionary<string, string>();
        if (root.TryGetProperty("Properties", out var propsElement) && propsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var member in propsElement.EnumerateObject())
            {
                properties[member.Name] = FlattenValue(member.Value);
            }
        }

        var template = templateProp.ValueKind == JsonValueKind.String ? templateProp.GetString() : null;
        var rendered = root.TryGetProperty("RenderedMessage", out var renderedProp) && renderedProp.ValueKind == JsonValueKind.String
            ? renderedProp.GetString()!
            : RenderTemplate(template, properties);

        var traceId = root.TryGetProperty("TraceId", out var trProp) && trProp.ValueKind == JsonValueKind.String ? trProp.GetString() : null;
        var spanId = root.TryGetProperty("SpanId", out var spProp) && spProp.ValueKind == JsonValueKind.String ? spProp.GetString() : null;
        PromoteTraceFields(properties, ref traceId, ref spanId);

        return new StructuredLogEvent(timestamp, level, template, rendered, exception, properties, traceId, spanId);
    }

    /// <summary>Resolves the event's trace/span id: prefers Serilog's native <c>Activity</c>-based fields
    /// (<c>@tr</c>/<c>@sp</c>, or the standard formatter's top-level <c>TraceId</c>/<c>SpanId</c>), falling back
    /// to a plain <c>TraceId</c>/<c>SpanId</c> property (e.g. from an enricher) when native ones are absent — then
    /// makes sure the resolved values are also reachable as ordinary properties, so the existing property-filter,
    /// correlation-key, search, and export machinery (all of which only look at <see cref="StructuredLogEvent.Properties"/>)
    /// keeps working uniformly regardless of which form produced them.</summary>
    private static void PromoteTraceFields(Dictionary<string, string> properties, ref string? traceId, ref string? spanId)
    {
        if (traceId is null && properties.TryGetValue("TraceId", out var propTraceId))
        {
            traceId = propTraceId;
        }

        if (spanId is null && properties.TryGetValue("SpanId", out var propSpanId))
        {
            spanId = propSpanId;
        }

        if (traceId is not null)
        {
            properties["TraceId"] = traceId;
        }

        if (spanId is not null)
        {
            properties["SpanId"] = spanId;
        }
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result)
            ? result
            : null;
    }

    private static string FlattenValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };

    /// <summary>Substitutes <c>{Name}</c>/<c>{@Name}</c>/<c>{$Name}</c>/<c>{Name:format}</c> tokens (and
    /// positional <c>{0}</c> tokens) with property values. Unknown tokens are left as-is.</summary>
    private static string RenderTemplate(string? template, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TemplateTokenPattern.Replace(template, match =>
        {
            var name = match.Groups[2].Value;
            return properties.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Splits a <see cref="StructuredLogEvent"/>'s rendered message into alternating literal-text and
    /// value segments so callers can render each part with a distinct color.
    /// </summary>
    /// <remarks>
    /// When the event has a <see cref="StructuredLogEvent.MessageTemplate"/> the split is driven by the
    /// original template tokens; each matched token is a value segment whose
    /// <see cref="StructuredMessageSegment.PropertyName"/> identifies the originating property.
    /// When there is no template (e.g. a pre-rendered <c>@m</c> message) the entire text is returned
    /// as a single literal segment.
    /// </remarks>
    public static IReadOnlyList<StructuredMessageSegment> SplitIntoSegments(
        StructuredLogEvent evt)
    {
        var template = evt.MessageTemplate;
        if (string.IsNullOrEmpty(template))
        {
            return [new StructuredMessageSegment(evt.RenderedMessage, null)];
        }

        var segments = new List<StructuredMessageSegment>();
        var lastIndex = 0;

        foreach (Match match in TemplateTokenPattern.Matches(template))
        {
            // Literal text before this token
            if (match.Index > lastIndex)
            {
                segments.Add(new StructuredMessageSegment(template[lastIndex..match.Index], null));
            }

            var propertyName = match.Groups[2].Value;
            var value = evt.Properties.TryGetValue(propertyName, out var v) ? v : match.Value;
            segments.Add(new StructuredMessageSegment(value, propertyName));

            lastIndex = match.Index + match.Length;
        }

        // Remaining literal tail
        if (lastIndex < template.Length)
        {
            segments.Add(new StructuredMessageSegment(template[lastIndex..], null));
        }

        return segments.Count > 0 ? segments : [new StructuredMessageSegment(evt.RenderedMessage, null)];
    }
}
