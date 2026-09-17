using System.Text.Json;
using LogViewer.App.Models;

namespace LogViewer.App.Services;

/// <summary>Pure formatting for exporting/copying a set of displayed lines — kept separate from the
/// file/clipboard I/O in <see cref="Views.Documents.TailDocumentView"/> so the shape of the output is
/// testable without WPF.</summary>
public static class LogLineExportFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ToPlainText(IEnumerable<LogLineViewModel> lines) =>
        string.Join(Environment.NewLine, lines.Select(l => l.Text));

    /// <summary>Structured lines serialize their parsed fields; plain lines fall back to
    /// <c>{ lineNumber, text }</c> so no information is silently dropped.</summary>
    public static string ToJson(IEnumerable<LogLineViewModel> lines)
    {
        var entries = lines.Select(line => line.Structured is { } structured
            ? (object)new
            {
                lineNumber = line.LineNumber,
                timestamp = structured.Timestamp,
                level = structured.Level,
                message = structured.RenderedMessage,
                exception = structured.Exception,
                properties = structured.Properties,
            }
            : new
            {
                lineNumber = line.LineNumber,
                text = line.Text,
            });

        return JsonSerializer.Serialize(entries, JsonOptions);
    }

    /// <summary>Renders each line the way the structured-view columns show it — <c>timestamp [Level] message</c>
    /// (plus an exception line, when present) — instead of its raw JSON/logfmt/etc. source text. A line that
    /// never parsed as structured copies unchanged, since there's nothing to reformat.</summary>
    public static string ToFormattedText(IEnumerable<LogLineViewModel> lines) =>
        string.Join(Environment.NewLine, lines.Select(FormatLine));

    private static string FormatLine(LogLineViewModel line)
    {
        if (line.Structured is not { } structured)
        {
            return line.Text;
        }

        var timestamp = structured.Timestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "";
        var level = string.IsNullOrEmpty(structured.Level) ? "" : $"[{structured.Level}] ";
        var formatted = $"{timestamp} {level}{structured.RenderedMessage}".TrimStart();

        return string.IsNullOrEmpty(structured.Exception) ? formatted : $"{formatted}{Environment.NewLine}{structured.Exception}";
    }
}
