using System.Globalization;
using System.Text;
using LogViewer.Core.Analysis;

namespace LogViewer.Core.Reporting;

/// <summary>The report's fixed wording, so the app can pass its UI language. <see cref="LinesRange"/>, <see cref="Line"/>
/// and <see cref="Occurrences"/> are composite format strings; the defaults are English.</summary>
public sealed record IncidentReportLabels
{
    public string Heading { get; init; } = "Incident report";
    public string File { get; init; } = "File";
    public string Generated { get; init; } = "Generated";
    public string LinesInFile { get; init; } = "Lines in file";
    public string Bookmarks { get; init; } = "Bookmarks";
    public string Notes { get; init; } = "Notes";
    public string MarkedLines { get; init; } = "Marked lines";
    public string NoMarkedLines { get; init; } = "No bookmarked, annotated or selected lines.";
    public string LinesRange { get; init; } = "Lines {0}–{1}";
    public string Line { get; init; } = "Line {0}";
    public string Exceptions { get; init; } = "Exceptions";
    public string NoExceptions { get; init; } = "No exceptions found in the file.";
    public string ShowingTopGroups { get; init; } = "The {0} most frequent of {1} distinct exceptions.";
    public string Occurrences { get; init; } = "{0}×";
    public string FirstLine { get; init; } = "First line";
    public string LastLine { get; init; } = "Last line";
    public string FirstSeen { get; init; } = "First seen";
    public string LastSeen { get; init; } = "Last seen";
    public string TopFrame { get; init; } = "Top frame";
}

/// <summary>Renders an <see cref="IncidentReport"/> as Markdown (for tickets/PRs/chat) or a self-contained HTML page.</summary>
public static class IncidentReportFormatter
{
    public static string ToMarkdown(IncidentReport report, IncidentReportLabels? labels = null)
    {
        labels ??= new IncidentReportLabels();
        var sb = new StringBuilder();
        sb.Append("# ").Append(labels.Heading).Append(" — ").AppendLine(MarkdownInline(report.Title)).AppendLine();
        sb.Append("- **").Append(labels.File).Append(":** ").AppendLine(CodeSpan(report.SourcePath));
        sb.Append("- **").Append(labels.Generated).Append(":** ").AppendLine(Time(report.GeneratedAt));
        sb.Append("- **").Append(labels.LinesInFile).Append(":** ").AppendLine(Number(report.TotalLines));
        sb.Append("- **").Append(labels.Bookmarks).Append(":** ").Append(Number(report.BookmarkCount))
            .Append(" · **").Append(labels.Notes).Append(":** ").AppendLine(Number(report.NoteCount));
        sb.AppendLine();

        sb.Append("## ").AppendLine(labels.MarkedLines).AppendLine();
        if (report.Excerpts.Count == 0)
        {
            sb.Append('_').Append(labels.NoMarkedLines).AppendLine("_").AppendLine();
        }

        var width = NumberWidth(report);
        foreach (var excerpt in report.Excerpts)
        {
            sb.Append("### ").AppendLine(RangeTitle(excerpt, labels)).AppendLine();
            // One paragraph per note (an empty ">" line between them), or Markdown would run them together.
            var notes = excerpt.Lines.Where(l => l.Note is not null)
                .Select(l => $"> **{Format(labels.Line, l.LineNumber)}:** {MarkdownInline(l.Note!)}")
                .ToList();
            if (notes.Count > 0)
            {
                sb.AppendLine(string.Join("\n>\n", notes)).AppendLine();
            }

            var body = string.Join('\n', excerpt.Lines.Select(l => $"{(l.IsMarked ? '►' : ' ')} {Number(l.LineNumber).PadLeft(width)}  {l.Text}"));
            AppendFence(sb, body, "text");
        }

        sb.Append("## ").AppendLine(labels.Exceptions).AppendLine();
        if (report.ExceptionGroups.Count == 0)
        {
            sb.Append('_').Append(labels.NoExceptions).AppendLine("_").AppendLine();
            return sb.ToString().ReplaceLineEndings();
        }

        sb.AppendLine(Format(labels.ShowingTopGroups, report.ExceptionGroups.Count, report.TotalExceptionGroups)).AppendLine();
        var rank = 0;
        foreach (var group in report.ExceptionGroups)
        {
            sb.Append("### ").Append(++rank).Append(". ").Append(MarkdownInline(group.ExceptionType))
                .Append(" (").Append(Format(labels.Occurrences, group.Count)).AppendLine(")").AppendLine();
            if (!string.IsNullOrWhiteSpace(group.SampleMessage))
            {
                sb.AppendLine(MarkdownInline(group.SampleMessage)).AppendLine();
            }

            foreach (var (label, value, isCode) in GroupFacts(group, labels))
            {
                sb.Append("- **").Append(label).Append(":** ").AppendLine(isCode ? CodeSpan(value) : value);
            }

            sb.AppendLine();
            AppendFence(sb, group.SampleText.TrimEnd(), "text");
        }

        // Excerpts and notes are joined with \n while AppendLine writes the platform newline; use one throughout.
        return sb.ToString().ReplaceLineEndings();
    }

    public static string ToHtml(IncidentReport report, IncidentReportLabels? labels = null)
    {
        labels ??= new IncidentReportLabels();
        var sb = new StringBuilder();
        var title = $"{labels.Heading} — {report.Title}";
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>").Append(H(title)).AppendLine("</title>");
        sb.AppendLine("""
            <style>
            body { font-family: "Segoe UI", system-ui, sans-serif; margin: 24px auto; max-width: 1100px; padding: 0 16px; color: #1f2328; background: #fff; }
            h1 { font-size: 1.6em; } h2 { margin-top: 1.6em; border-bottom: 1px solid #d0d7de; padding-bottom: .3em; } h3 { font-size: 1.05em; margin-top: 1.4em; }
            dl.meta { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; } dl.meta dt { font-weight: 600; } dl.meta dd { margin: 0; min-width: 0; overflow-wrap: anywhere; }
            pre { background: #f6f8fa; border: 1px solid #d0d7de; border-radius: 6px; padding: 8px 0; overflow-x: auto; font: 12px/1.45 Consolas, "Cascadia Mono", monospace; }
            pre > code { display: inline-block; min-width: 100%; box-sizing: border-box; font: inherit; }
            .l { display: block; padding: 0 12px; white-space: pre; } .l.m { background: #fff8c5; } .n { color: #6e7781; user-select: none; }
            .note { background: #dafbe1; border-left: 4px solid #1a7f37; padding: 6px 10px; margin: 4px 0; }
            .facts { color: #57606a; } .empty { color: #57606a; font-style: italic; }
            @media (prefers-color-scheme: dark) {
              body { color: #e6edf3; background: #0d1117; } h2 { border-color: #30363d; }
              pre { background: #161b22; border-color: #30363d; } .l.m { background: #3b2e00; } .n { color: #8b949e; }
              .note { background: #12261e; } .facts, .empty { color: #8b949e; }
            }
            </style>
            """);
        sb.AppendLine("</head><body>");
        sb.Append("<h1>").Append(H(title)).AppendLine("</h1>");
        sb.AppendLine("<dl class=\"meta\">");
        AppendMeta(sb, labels.File, $"<code>{H(report.SourcePath)}</code>");
        AppendMeta(sb, labels.Generated, H(Time(report.GeneratedAt)));
        AppendMeta(sb, labels.LinesInFile, Number(report.TotalLines));
        AppendMeta(sb, labels.Bookmarks, Number(report.BookmarkCount));
        AppendMeta(sb, labels.Notes, Number(report.NoteCount));
        sb.AppendLine("</dl>");

        sb.Append("<h2>").Append(H(labels.MarkedLines)).AppendLine("</h2>");
        if (report.Excerpts.Count == 0)
        {
            sb.Append("<p class=\"empty\">").Append(H(labels.NoMarkedLines)).AppendLine("</p>");
        }

        var width = NumberWidth(report);
        foreach (var excerpt in report.Excerpts)
        {
            sb.Append("<h3>").Append(H(RangeTitle(excerpt, labels))).AppendLine("</h3>");
            foreach (var line in excerpt.Lines.Where(l => l.Note is not null))
            {
                sb.Append("<div class=\"note\"><strong>").Append(H(Format(labels.Line, line.LineNumber))).Append(":</strong> ")
                    .Append(H(line.Note!)).AppendLine("</div>");
            }

            sb.Append("<pre><code>");
            foreach (var line in excerpt.Lines)
            {
                sb.Append(line.IsMarked ? "<span class=\"l m\">" : "<span class=\"l\">")
                    .Append("<span class=\"n\">").Append(Number(line.LineNumber).PadLeft(width)).Append("  </span>")
                    .Append(H(line.Text)).Append("</span>");
            }

            sb.AppendLine("</code></pre>");
        }

        sb.Append("<h2>").Append(H(labels.Exceptions)).AppendLine("</h2>");
        if (report.ExceptionGroups.Count == 0)
        {
            sb.Append("<p class=\"empty\">").Append(H(labels.NoExceptions)).AppendLine("</p>");
        }
        else
        {
            sb.Append("<p>").Append(H(Format(labels.ShowingTopGroups, report.ExceptionGroups.Count, report.TotalExceptionGroups))).AppendLine("</p>");
            var rank = 0;
            foreach (var group in report.ExceptionGroups)
            {
                sb.Append("<h3>").Append(++rank).Append(". ").Append(H(group.ExceptionType))
                    .Append(" (").Append(H(Format(labels.Occurrences, group.Count))).AppendLine(")</h3>");
                if (!string.IsNullOrWhiteSpace(group.SampleMessage))
                {
                    sb.Append("<p>").Append(H(group.SampleMessage)).AppendLine("</p>");
                }

                sb.Append("<p class=\"facts\">")
                    .Append(string.Join(" · ", GroupFacts(group, labels).Select(f =>
                        $"<strong>{H(f.Label)}:</strong> {(f.IsCode ? $"<code>{H(f.Value)}</code>" : H(f.Value))}")))
                    .AppendLine("</p>");
                sb.Append("<pre><code><span class=\"l\">").Append(H(group.SampleText.TrimEnd())).AppendLine("</span></code></pre>");
            }
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static IEnumerable<(string Label, string Value, bool IsCode)> GroupFacts(ExceptionGroup group, IncidentReportLabels labels)
    {
        yield return (labels.FirstLine, Number(group.FirstLineNumber), false);
        yield return (labels.LastLine, Number(group.LastLineNumber), false);
        if (group.FirstSeen is { } first)
        {
            yield return (labels.FirstSeen, Time(first), false);
        }

        if (group.LastSeen is { } last)
        {
            yield return (labels.LastSeen, Time(last), false);
        }

        if (!string.IsNullOrWhiteSpace(group.TopFrame))
        {
            yield return (labels.TopFrame, group.TopFrame, true);
        }
    }

    private static string RangeTitle(IncidentReportExcerpt excerpt, IncidentReportLabels labels) =>
        excerpt.FirstLineNumber == excerpt.LastLineNumber
            ? Format(labels.Line, excerpt.FirstLineNumber)
            : Format(labels.LinesRange, excerpt.FirstLineNumber, excerpt.LastLineNumber);

    private static int NumberWidth(IncidentReport report) =>
        report.Excerpts.Count == 0 ? 1 : report.Excerpts.Max(e => Number(e.LastLineNumber).Length);

    private static void AppendMeta(StringBuilder sb, string label, string htmlValue) =>
        sb.Append("<dt>").Append(H(label)).Append("</dt><dd>").Append(htmlValue).AppendLine("</dd>");

    /// <summary>A fenced block whose fence is longer than any backtick run in <paramref name="body"/>, so log text
    /// containing ``` can't end it early.</summary>
    private static void AppendFence(StringBuilder sb, string body, string language)
    {
        var fence = new string('`', Math.Max(3, LongestRun(body, '`') + 1));
        sb.Append(fence).AppendLine(language).AppendLine(body).AppendLine(fence).AppendLine();
    }

    private static string CodeSpan(string text)
    {
        var ticks = new string('`', LongestRun(text, '`') + 1);
        var pad = text.StartsWith('`') || text.EndsWith('`') ? " " : string.Empty;
        return ticks + pad + text.ReplaceLineEndings(" ") + pad + ticks;
    }

    private static int LongestRun(string text, char c)
    {
        int longest = 0, current = 0;
        foreach (var ch in text)
        {
            current = ch == c ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    /// <summary>Single-line text safe inside Markdown prose: markup characters escaped, so a log message can't turn
    /// into links, emphasis or HTML.</summary>
    private static string MarkdownInline(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text.ReplaceLineEndings(" "))
        {
            if (ch is '\\' or '`' or '*' or '_' or '[' or ']' or '<' or '>' or '#' or '|' or '~')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>Escapes only what HTML needs (unlike <c>WebUtility.HtmlEncode</c>, which also turns every non-ASCII
    /// letter into a numeric entity); the page is UTF-8.</summary>
    private static string H(string text)
    {
        var sb = new StringBuilder(text.Length + 16);
        foreach (var ch in text)
        {
            sb.Append(ch switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                _ => ch.ToString(),
            });
        }

        return sb.ToString();
    }

    private static string Time(DateTimeOffset value) => value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);
}
