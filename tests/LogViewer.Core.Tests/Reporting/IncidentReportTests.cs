using LogViewer.Core.Annotations;
using LogViewer.Core.Reporting;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Reporting;

public sealed class IncidentReportTests
{
    private static readonly string Log = string.Join('\n',
    [
        "2026-09-23 10:00:00 INFO start",                                      // 1
        "2026-09-23 10:00:01 INFO a",                                          // 2
        "2026-09-23 10:00:02 INFO b",                                          // 3
        "2026-09-23 10:00:03 ERROR payment failed",                            // 4
        "System.TimeoutException: gateway timed out",                          // 5
        "   at Shop.Payments.Gateway.Charge()",                                // 6
        "2026-09-23 10:00:04 INFO c",                                          // 7
        "2026-09-23 10:00:05 INFO d",                                          // 8
        "2026-09-23 10:00:06 INFO e",                                          // 9
        "2026-09-23 10:00:07 INFO f",                                          // 10
        "2026-09-23 10:00:08 INFO g",                                          // 11
        "2026-09-23 10:00:09 ERROR payment failed again",                      // 12
        "System.TimeoutException: gateway timed out",                          // 13
        "   at Shop.Payments.Gateway.Charge()",                                // 14
        "2026-09-23 10:00:10 INFO `rm -rf` <script>alert(1)</script> ```",     // 15
    ]) + "\n";

    [Fact]
    public void MergeWindows_JoinsOverlappingAndTouchingContext_AndClampsToTheFile()
    {
        Assert.Equal([(1L, 6L), (8L, 12L), (18L, 21L)], IncidentReportBuilder.MergeWindows([1, 4, 10, 20], 2, 21));
        Assert.Equal([(2L, 11L)], IncidentReportBuilder.MergeWindows([4, 9], 2, 21)); // 2–6 and 7–11 touch
    }

    [Fact]
    public async Task Build_CollectsMarkedLinesWithContext_AndGroupsTheExceptions()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Log);
        var notes = new[]
        {
            new LineAnnotation(4, LineAnnotationStore.HashText("2026-09-23 10:00:03 ERROR payment failed"), "root cause", DateTimeOffset.Now),
            new LineAnnotation(9, LineAnnotationStore.HashText("what line 9 used to say"), "stale", DateTimeOffset.Now),
        };

        var report = await IncidentReportBuilder.BuildAsync(fixture.FilePath, "shop.log", [12], notes, [], contextLines: 1);

        Assert.Equal(15, report.TotalLines);
        Assert.Equal(1, report.BookmarkCount);
        Assert.Equal(1, report.NoteCount); // the note on line 9 no longer matches its line
        Assert.Equal([(3L, 5L), (11L, 13L)], report.Excerpts.Select(e => (e.FirstLineNumber, e.LastLineNumber)));
        var noted = report.Excerpts[0].Lines.Single(l => l.IsMarked);
        Assert.Equal((4L, "root cause", false), (noted.LineNumber, noted.Note, noted.IsBookmarked));
        Assert.True(report.Excerpts[1].Lines.Single(l => l.IsMarked).IsBookmarked);

        var group = Assert.Single(report.ExceptionGroups);
        Assert.Equal("System.TimeoutException", group.ExceptionType);
        Assert.Equal(2, group.Count);
    }

    [Fact]
    public async Task Markdown_EscapesLogTextSoItCannotBreakTheDocument()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Log);
        var report = await IncidentReportBuilder.BuildAsync(fixture.FilePath, "shop_[prod].log", [15], [], [], contextLines: 0);

        var markdown = IncidentReportFormatter.ToMarkdown(report);

        Assert.Contains("# Incident report — shop\\_\\[prod\\].log", markdown);
        Assert.Contains("````text\n► 15  2026-09-23 10:00:10 INFO `rm -rf` <script>alert(1)</script> ```\n````", markdown.ReplaceLineEndings("\n"));
        Assert.Contains("### 1. System.TimeoutException (2×)", markdown);
        Assert.Contains("- **Top frame:** `Shop.Payments.Gateway.Charge()`", markdown);
    }

    [Fact]
    public async Task Markdown_KeepsConsecutiveNotesAsSeparateParagraphs()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Log);
        var lines = Log.Split('\n');
        var notes = new[]
        {
            new LineAnnotation(4, LineAnnotationStore.HashText(lines[3]), "root cause", DateTimeOffset.Now),
            new LineAnnotation(5, LineAnnotationStore.HashText(lines[4]), "[AI] same as 13", DateTimeOffset.Now),
        };
        var report = await IncidentReportBuilder.BuildAsync(fixture.FilePath, "shop.log", [], notes, [], contextLines: 0);

        var markdown = IncidentReportFormatter.ToMarkdown(report).ReplaceLineEndings("\n");

        Assert.Contains("> **Line 4:** root cause\n>\n> **Line 5:** \\[AI\\] same as 13\n", markdown);
    }

    [Fact]
    public async Task Html_EncodesLogText_AndMarksTheAnnotatedLines()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Log);
        var notes = new[] { new LineAnnotation(15, LineAnnotationStore.HashText(Log.Split('\n')[14]), "<b>odd</b>", DateTimeOffset.Now) };
        var report = await IncidentReportBuilder.BuildAsync(fixture.FilePath, "shop.log", [], notes, [], contextLines: 1);

        var html = IncidentReportFormatter.ToHtml(report, new IncidentReportLabels { Heading = "Relatório de incidente" });

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<title>Relatório de incidente — shop.log</title>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("<strong>Line 15:</strong> &lt;b&gt;odd&lt;/b&gt;", html);
        Assert.Contains("<span class=\"l m\"><span class=\"n\">15  </span>", html);
        Assert.Contains("<span class=\"l\"><span class=\"n\">14  </span>", html);
    }

    [Fact]
    public async Task Report_WithNothingMarked_StillListsTheExceptions()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(Log);
        var report = await IncidentReportBuilder.BuildAsync(fixture.FilePath, "shop.log", [], [], []);

        var markdown = IncidentReportFormatter.ToMarkdown(report);

        Assert.Empty(report.Excerpts);
        Assert.Contains("_No bookmarked, annotated or selected lines._", markdown);
        Assert.Contains("The 1 most frequent of 1 distinct exceptions.", markdown);
    }
}
