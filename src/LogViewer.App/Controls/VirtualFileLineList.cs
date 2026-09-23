using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using LogViewer.App.Models;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Indexing;

namespace LogViewer.App.Controls;

/// <summary>
/// Data-virtualized view of an entire file for a virtualizing <c>ListView</c>: it reports the file's full line
/// count, but only materializes the pages of lines the list actually asks for (the visible viewport plus a
/// little scroll-ahead), reading them on demand through a <see cref="FileLineIndex"/> and keeping a small LRU of
/// recently used pages. WPF's <c>ListCollectionView</c> over a plain <see cref="IList"/> with no sort/filter/group
/// never enumerates the source, so a 100M-line file costs the same memory as a 1K-line one.
/// </summary>
public sealed class VirtualFileLineList : IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly FileLineIndex _index;
    private readonly Func<string, HighlightMatch?> _evaluateHighlight;
    private readonly int _pageSize;
    private readonly int _maxCachedPages;
    private readonly Dictionary<int, LinkedListNode<(int Page, LogLineViewModel[] Lines)>> _pages = new();
    private readonly LinkedList<(int Page, LogLineViewModel[] Lines)> _lru = new();
    private int _count;

    public VirtualFileLineList(FileLineIndex index, Func<string, HighlightMatch?> evaluateHighlight, int pageSize = 256, int maxCachedPages = 32)
    {
        _index = index;
        _evaluateHighlight = evaluateHighlight;
        _pageSize = pageSize;
        _maxCachedPages = maxCachedPages;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _count;

    /// <summary>Picks up the index's current line count (after an <see cref="FileLineIndex.UpdateAsync"/>) and
    /// raises a single Reset. <paramref name="contentChanged"/> drops every cached page (file rebuilt / highlight
    /// rules changed); otherwise only the last page is dropped, since it may have been partial.</summary>
    public void Refresh(bool contentChanged = false)
    {
        if (contentChanged)
        {
            _pages.Clear();
            _lru.Clear();
        }
        else if (_count > 0)
        {
            Evict((_count - 1) / _pageSize);
        }

        _count = (int)Math.Min(int.MaxValue, _index.LineCount);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public LogLineViewModel this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);

            var page = GetPage(index / _pageSize);
            var offset = index % _pageSize;
            return offset < page.Length ? page[offset] : Placeholder(index);
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public int IndexOf(object? value) =>
        value is LogLineViewModel line && line.LineNumber >= 1 && line.LineNumber <= _count ? (int)(line.LineNumber - 1) : -1;

    public bool Contains(object? value) => IndexOf(value) >= 0;

    private LogLineViewModel[] GetPage(int page)
    {
        if (_pages.TryGetValue(page, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Lines;
        }

        var lines = _index.ReadLines((long)page * _pageSize + 1, _pageSize)
            .Select(l => new LogLineViewModel(l.LineNumber, l.Text, structured: null, _evaluateHighlight(l.Text), isBookmarked: false))
            .ToArray();

        var added = _lru.AddFirst((page, lines));
        _pages[page] = added;
        while (_lru.Count > _maxCachedPages)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _pages.Remove(last.Value.Page);
        }

        return lines;
    }

    private void Evict(int page)
    {
        if (_pages.Remove(page, out var node))
        {
            _lru.Remove(node);
        }
    }

    private static LogLineViewModel Placeholder(int index) => new(index + 1L, string.Empty, structured: null, match: null, isBookmarked: false);

    public IEnumerator GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    public bool IsFixedSize => false;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();

    public void CopyTo(Array array, int index) => throw new NotSupportedException();
}
