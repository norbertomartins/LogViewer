using System.ComponentModel;
using System.Globalization;
using LogViewer.Core.Analysis;
using LogViewer.Core.Documents;
using LogViewer.Core.Indexing;
using LogViewer.Core.Structured;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

public sealed record BookmarkDto(long LineNumber, string? Text);

public sealed record DocumentBookmarks(string SourcePath, string Title, IReadOnlyList<BookmarkDto> Bookmarks, bool Truncated);

public sealed record TimeRangeToolResult(string? From, string? To, IReadOnlyList<SearchResultDto> Lines, bool Truncated, string? Error);

[McpServerToolType]
public sealed class LogNavigationTools(IOpenDocumentCatalog documentCatalog)
{
    [McpServerTool(Name = "logs_get_bookmarks")]
    [Description(
        "Lists the lines the user has bookmarked in each open LogViewer document, with the line text — the user's " +
        "own markers of what they consider interesting, so start here when asked about 'the lines I marked'.")]
    public async Task<IReadOnlyList<DocumentBookmarks>> GetBookmarks(
        [Description("Maximum bookmarks to return per document.")] int maxResults,
        CancellationToken cancellationToken)
    {
        var cap = ResponseLimits.ClampRows(maxResults);
        var result = new List<DocumentBookmarks>();
        foreach (var document in documentCatalog.GetOpenDocuments())
        {
            var bookmarks = document.BookmarkedLineNumbers ?? [];
            if (bookmarks.Count == 0)
            {
                continue;
            }

            FileLineIndex? index = null;
            if (document.SearchableFilePath is { } path && File.Exists(path))
            {
                index = await FileLineIndexCache.GetAsync(path, cancellationToken).ConfigureAwait(false);
            }

            var dtos = bookmarks.Take(cap)
                .Select(n => new BookmarkDto(n, index?.ReadLines(n, 1).FirstOrDefault().Text is { } text ? ResponseLimits.Truncate(text) : null))
                .ToList();
            result.Add(new DocumentBookmarks(document.SearchableFilePath ?? document.SourcePath, document.Title, dtos, bookmarks.Count > cap));
        }

        return result;
    }

    [McpServerTool(Name = "logs_query_time_range")]
    [Description(
        "Returns the lines of a log file between two times, found by binary search over a cached line index — cheap " +
        "even on multi-GB files. 'from'/'to' accept ISO-8601 ('2026-09-23T14:05:00Z'), 'yyyy-MM-dd HH:mm[:ss]', a time " +
        "of day ('14:05'), or an offset relative to the newest line in the file ('-15m', '-1h30m'); naive times are " +
        "read in the log's own offset. Omit 'to' to read up to the end. Untimestamped continuation lines (stack " +
        "traces) are included with their entry. Use it for 'what happened between X and Y' or 'the last N minutes'.")]
    public async Task<TimeRangeToolResult> QueryTimeRange(
        [Description("Full path to the log file (any text format).")] string sourcePath,
        [Description("Range start (see the tool description for accepted forms).")] string from,
        [Description("Range end, or null/empty for the end of the file.")] string? to,
        [Description("Maximum number of lines to return.")] int maxResults,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return new TimeRangeToolResult(null, null, [], false, $"File not found: {sourcePath}");
        }

        var cap = ResponseLimits.ClampRows(maxResults);
        var index = await FileLineIndexCache.GetAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var extract = TimeRangeReader.CreateExtractor(LogLineParsers.DetectFile(sourcePath));
        var latest = TimeRangeReader.LatestTimestamp(index, extract);

        if (!TimestampQuery.TryParse(from, latest, out var start))
        {
            return new TimeRangeToolResult(null, null, [], false, $"Could not parse 'from': {from}");
        }

        var end = DateTimeOffset.MaxValue;
        if (!string.IsNullOrWhiteSpace(to) && !TimestampQuery.TryParse(to, latest, out end))
        {
            return new TimeRangeToolResult(null, null, [], false, $"Could not parse 'to': {to}");
        }

        var range = await Task.Run(() => TimeRangeReader.Read(index, start, end, cap, extract, cancellationToken), cancellationToken).ConfigureAwait(false);
        return new TimeRangeToolResult(
            start.ToString("O", CultureInfo.InvariantCulture),
            end == DateTimeOffset.MaxValue ? null : end.ToString("O", CultureInfo.InvariantCulture),
            range.Lines.Select(l => new SearchResultDto(l.LineNumber, ResponseLimits.Truncate(l.Text))).ToList(),
            range.Truncated,
            null);
    }
}
