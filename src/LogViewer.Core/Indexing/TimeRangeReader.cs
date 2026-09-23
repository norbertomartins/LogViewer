using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Indexing;

/// <summary>Lines of a file whose timestamps fall in a range — see <see cref="TimeRangeReader"/>.</summary>
public sealed record TimeRangeResult(IReadOnlyList<IndexedLine> Lines, bool Truncated);

/// <summary>
/// Reads every line between two times from an indexed file: binary-searches to the first line at or after
/// <c>from</c> (<see cref="FileLineIndex.FindFirstLineAtOrAfter"/>), then reads forward until a line is stamped after
/// <c>to</c>. Untimestamped lines inside the range (stack frames, wrapped messages) are kept with their entry.
/// </summary>
public static class TimeRangeReader
{
    public static TimeRangeResult Read(
        FileLineIndex index, DateTimeOffset from, DateTimeOffset to, int maxLines,
        Func<string, DateTimeOffset?> extractTimestamp, CancellationToken cancellationToken = default)
    {
        var lines = new List<IndexedLine>();
        if (to < from || maxLines <= 0 || index.FindFirstLineAtOrAfter(from, extractTimestamp, cancellationToken) is not { } first)
        {
            return new TimeRangeResult(lines, false);
        }

        var next = first;
        var total = index.LineCount;
        while (next <= total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = index.ReadLines(next, index.Stride);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var line in page)
            {
                if (extractTimestamp(line.Text) is { } ts && ts > to)
                {
                    return new TimeRangeResult(lines, false);
                }

                if (lines.Count == maxLines)
                {
                    return new TimeRangeResult(lines, true);
                }

                lines.Add(line);
            }

            next = page[^1].LineNumber + 1;
        }

        return new TimeRangeResult(lines, false);
    }

    /// <summary>Newest timestamp among the last lines of the file — the reference for relative queries like "-15m".</summary>
    public static DateTimeOffset? LatestTimestamp(FileLineIndex index, Func<string, DateTimeOffset?> extractTimestamp, int tailLines = 500)
    {
        var from = Math.Max(1, index.LineCount - tailLines + 1);
        DateTimeOffset? latest = null;
        foreach (var line in index.ReadLines(from, tailLines))
        {
            if (extractTimestamp(line.Text) is { } ts)
            {
                latest = ts;
            }
        }

        return latest;
    }

    /// <summary>A line → timestamp function for a file: the parsed event's timestamp when the line matches
    /// <paramref name="formatId"/>'s parser, else a timestamp extracted from the raw text. Each call builds its own
    /// parser, so the returned function can run on a background thread (parsers aren't thread-safe).</summary>
    public static Func<string, DateTimeOffset?> CreateExtractor(string? formatId)
    {
        var parser = LogLineParsers.Create(formatId);
        return text => parser is not null && parser.TryParse(text, out var evt) && evt?.Timestamp is { } ts
            ? ts
            : MergedTimestampExtractor.TryExtract(text);
    }
}
