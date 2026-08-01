namespace LogViewer.Core.Bookmarks;

/// <summary>
/// Tracks bookmarked line numbers for one document and provides O(log n) next/previous navigation
/// relative to a given line, rather than a linear scan of the buffer.
/// </summary>
public sealed class BookmarkManager
{
    private readonly SortedSet<long> _lineNumbers = new();
    private readonly Dictionary<long, Bookmark> _byLineNumber = new();

    public IReadOnlyCollection<Bookmark> Bookmarks => _byLineNumber.Values;

    public bool IsBookmarked(long lineNumber) => _lineNumbers.Contains(lineNumber);

    public Bookmark Toggle(long lineNumber, string? note = null)
    {
        if (_byLineNumber.Remove(lineNumber, out var existing))
        {
            _lineNumbers.Remove(lineNumber);
            return existing;
        }

        var bookmark = new Bookmark(Guid.NewGuid(), lineNumber, DateTimeOffset.UtcNow, note);
        _byLineNumber[lineNumber] = bookmark;
        _lineNumbers.Add(lineNumber);
        return bookmark;
    }

    public void Clear()
    {
        _lineNumbers.Clear();
        _byLineNumber.Clear();
    }

    /// <summary>Nearest bookmarked line strictly after <paramref name="afterLineNumber"/>, if any. O(log n).</summary>
    public long? Next(long afterLineNumber)
    {
        var view = _lineNumbers.GetViewBetween(afterLineNumber + 1, long.MaxValue);
        return view.Count > 0 ? view.Min : null;
    }

    /// <summary>Nearest bookmarked line strictly before <paramref name="beforeLineNumber"/>, if any. O(log n).</summary>
    public long? Previous(long beforeLineNumber)
    {
        if (beforeLineNumber <= long.MinValue)
        {
            return null;
        }

        var view = _lineNumbers.GetViewBetween(long.MinValue, beforeLineNumber - 1);
        return view.Count > 0 ? view.Max : null;
    }
}
