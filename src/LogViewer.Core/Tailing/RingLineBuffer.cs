using System.Collections;

namespace LogViewer.Core.Tailing;

/// <summary>
/// A bounded, FIFO-evicting buffer of <see cref="TailLine"/>s. Capacity is independent of the
/// underlying file's size, which is what keeps memory usage bounded for very large or fast-growing
/// logs. Deliberately does not implement <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>
/// per item — at high line rates that alone would overwhelm the UI thread, so consumers batch updates instead.
/// </summary>
public sealed class RingLineBuffer : IReadOnlyList<TailLine>
{
    private readonly TailLine[] _items;
    private int _start;
    private int _count;

    public RingLineBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _items = new TailLine[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => _count;

    /// <summary>Total number of lines ever appended, including ones since evicted.</summary>
    public long TotalLinesAppended { get; private set; }

    public TailLine this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items[(_start + index) % _items.Length];
        }
    }

    public void Append(TailLine line)
    {
        var writeIndex = (_start + _count) % _items.Length;
        if (_count < _items.Length)
        {
            _items[writeIndex] = line;
            _count++;
        }
        else
        {
            _items[_start] = line;
            _start = (_start + 1) % _items.Length;
        }

        TotalLinesAppended++;
    }

    public void AppendRange(IReadOnlyList<TailLine> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            Append(lines[i]);
        }
    }

    public void Clear()
    {
        Array.Clear(_items);
        _start = 0;
        _count = 0;
        TotalLinesAppended = 0;
    }

    public IEnumerator<TailLine> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
