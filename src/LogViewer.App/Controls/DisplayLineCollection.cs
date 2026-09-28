using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using LogViewer.App.Models;

namespace LogViewer.App.Controls;

/// <summary>
/// A bounded, FIFO-evicting collection of <see cref="LogLineViewModel"/> mirroring the capacity of
/// the document's <see cref="LogViewer.Core.Tailing.RingLineBuffer"/>. Raises a single Reset
/// notification per <see cref="AppendRange"/>/<see cref="Clear"/> call rather than one per item —
/// WPF's list virtualization only re-renders the visible viewport on Reset, so this stays cheap even
/// at high line rates or large capacities.
/// <para>Implements the non-generic, read-only <see cref="IList"/> on purpose: WPF wraps an <see cref="IList"/>
/// source in a <see cref="System.Windows.Data.ListCollectionView"/> that indexes straight into it, whereas any
/// other <see cref="IEnumerable"/> gets an <c>EnumerableCollectionView</c> that re-copies every item into a
/// private snapshot collection on each Reset — i.e. O(capacity) work per tail flush.</para>
/// </summary>
public sealed class DisplayLineCollection : IReadOnlyList<LogLineViewModel>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly List<LogLineViewModel> _items;

    // Line number → absolute position (items ever appended since the last Clear); index = position - _evictedCount.
    // Positions stay valid across FIFO eviction, so the index is maintained incrementally instead of rebuilt per append.
    private readonly Dictionary<long, long> _positionByLineNumber = new();
    private readonly int _capacity;
    private long _evictedCount;

    public DisplayLineCollection(int capacity)
    {
        _capacity = capacity;
        _items = new List<LogLineViewModel>(Math.Min(capacity, 4096));
    }

    public int Count => _items.Count;

    public LogLineViewModel this[int index] => _items[index];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AppendRange(IReadOnlyList<LogLineViewModel> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var basePosition = _evictedCount + _items.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            _positionByLineNumber[lines[i].LineNumber] = basePosition + i;
        }

        _items.AddRange(lines);
        var overflow = _items.Count - _capacity;
        if (overflow > 0)
        {
            for (var i = 0; i < overflow; i++)
            {
                // Only drop the entry if it still points at the evicted item — a later duplicate line number wins.
                var lineNumber = _items[i].LineNumber;
                if (_positionByLineNumber.TryGetValue(lineNumber, out var position) && position == _evictedCount + i)
                {
                    _positionByLineNumber.Remove(lineNumber);
                }
            }

            _items.RemoveRange(0, overflow);
            _evictedCount += overflow;
        }

        RaiseReset();
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        _positionByLineNumber.Clear();
        _evictedCount = 0;
        RaiseReset();
    }

    /// <summary>O(1) lookup backed by <see cref="_positionByLineNumber"/>, maintained incrementally on append/evict
    /// rather than scanned per lookup — callers (search-result jumps, bookmark and highlight navigation) invoke this
    /// far more often than the collection mutates.</summary>
    public LogLineViewModel? FindByLineNumber(long lineNumber) =>
        _positionByLineNumber.TryGetValue(lineNumber, out var position) ? _items[(int)(position - _evictedCount)] : null;

    /// <summary>Position of <paramref name="line"/> in the collection (O(1) via the line-number index), or -1.</summary>
    public int IndexOf(LogLineViewModel line)
    {
        if (!_positionByLineNumber.TryGetValue(line.LineNumber, out var position))
        {
            return -1;
        }

        var index = (int)(position - _evictedCount);
        return ReferenceEquals(_items[index], line) ? index : -1;
    }

    private void RaiseReset()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<LogLineViewModel> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // --- Read-only IList (for WPF's ListCollectionView; mutation only goes through AppendRange/Clear) ---------------

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    bool IList.Contains(object? value) => value is LogLineViewModel line && IndexOf(line) >= 0;

    int IList.IndexOf(object? value) => value is LogLineViewModel line ? IndexOf(line) : -1;

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();
}
