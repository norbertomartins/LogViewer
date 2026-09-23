using LogViewer.Core.Analysis;
using LogViewer.Core.Annotations;
using LogViewer.Core.Indexing;

namespace LogViewer.Core.Reporting;

/// <summary>One line of an excerpt. <paramref name="IsMarked"/>: one of the lines the report is about (bookmarked, noted
/// or selected) rather than surrounding context.</summary>
public sealed record IncidentReportLine(long LineNumber, string Text, bool IsMarked, bool IsBookmarked, string? Note);

/// <summary>A run of consecutive lines around one or more marked lines (overlapping context windows are merged).</summary>
public sealed record IncidentReportExcerpt(IReadOnlyList<IncidentReportLine> Lines)
{
    public long FirstLineNumber => Lines[0].LineNumber;

    public long LastLineNumber => Lines[^1].LineNumber;
}

/// <summary>What <see cref="IncidentReportFormatter"/> renders: the user's marked lines with context, plus the file's
/// most frequent exception groups.</summary>
public sealed record IncidentReport(
    string Title,
    string SourcePath,
    DateTimeOffset GeneratedAt,
    long TotalLines,
    int BookmarkCount,
    int NoteCount,
    IReadOnlyList<IncidentReportExcerpt> Excerpts,
    IReadOnlyList<ExceptionGroup> ExceptionGroups,
    int TotalExceptionGroups);

public static class IncidentReportBuilder
{
    public const int DefaultContextLines = 2;
    public const int DefaultMaxExceptionGroups = 10;

    /// <summary>Most marked lines one report takes, so a document with thousands of bookmarks can't produce a huge file.</summary>
    public const int MaxMarkedLines = 500;

    /// <param name="notes">Notes whose line no longer has the text they were written on are left out.</param>
    /// <param name="selectedLineNumbers">Extra lines to include (e.g. the current selection), shown like bookmarks.</param>
    public static async Task<IncidentReport> BuildAsync(
        string path,
        string title,
        IReadOnlyCollection<long> bookmarkedLineNumbers,
        IReadOnlyCollection<LineAnnotation> notes,
        IReadOnlyCollection<long> selectedLineNumbers,
        int contextLines = DefaultContextLines,
        int maxExceptionGroups = DefaultMaxExceptionGroups,
        CancellationToken cancellationToken = default)
    {
        var index = await FileLineIndexCache.GetAsync(path, cancellationToken).ConfigureAwait(false);
        var total = index.LineCount;
        var bookmarks = bookmarkedLineNumbers.Where(n => n >= 1 && n <= total).ToHashSet();
        // Only notes whose line still has the text they were written on; a stale one must not pull in context either.
        var validNotes = notes
            .Where(n => n.LineNumber >= 1 && n.LineNumber <= total)
            .Where(n => index.ReadLines(n.LineNumber, 1) is [var line] && LineAnnotationStore.HashText(line.Text) == n.TextHash)
            .ToDictionary(n => n.LineNumber, n => n.Note);
        var marked = bookmarks
            .Concat(validNotes.Keys)
            .Concat(selectedLineNumbers.Where(n => n >= 1 && n <= total))
            .Distinct()
            .Order()
            .Take(MaxMarkedLines)
            .ToList();

        var excerpts = new List<IncidentReportExcerpt>();
        foreach (var (first, last) in MergeWindows(marked, Math.Max(0, contextLines), total))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lines = index.ReadLines(first, (int)(last - first + 1))
                .Select(line =>
                {
                    var note = validNotes.GetValueOrDefault(line.LineNumber);
                    var isBookmarked = bookmarks.Contains(line.LineNumber);
                    var isMarked = isBookmarked || note is not null || selectedLineNumbers.Contains(line.LineNumber);
                    return new IncidentReportLine(line.LineNumber, line.Text, isMarked, isBookmarked, note);
                })
                .ToList();
            excerpts.Add(new IncidentReportExcerpt(lines));
        }

        var groups = await ExceptionGrouper.GroupFileAsync(path, cancellationToken).ConfigureAwait(false);
        return new IncidentReport(
            title,
            path,
            DateTimeOffset.Now,
            total,
            bookmarks.Count,
            validNotes.Count,
            excerpts,
            [.. groups.OrderByDescending(g => g.Count).ThenBy(g => g.FirstLineNumber).Take(Math.Max(0, maxExceptionGroups))],
            groups.Count);
    }

    /// <summary>[line - context, line + context] windows for each marked line, merged where they overlap or touch.</summary>
    internal static IEnumerable<(long First, long Last)> MergeWindows(IReadOnlyList<long> sortedLines, int context, long totalLines)
    {
        long? first = null;
        long last = 0;
        foreach (var line in sortedLines)
        {
            var from = Math.Max(1, line - context);
            var to = Math.Min(totalLines, line + context);
            if (first is not null && from <= last + 1)
            {
                last = Math.Max(last, to);
                continue;
            }

            if (first is not null)
            {
                yield return (first.Value, last);
            }

            first = from;
            last = to;
        }

        if (first is not null)
        {
            yield return (first.Value, last);
        }
    }
}
