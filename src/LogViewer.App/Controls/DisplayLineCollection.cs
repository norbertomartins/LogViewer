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
/// </summary>
public sealed class DisplayLineCollection : IReadOnlyList<LogLineViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly List<LogLineViewModel> _items;
    private readonly int _capacity;

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

        _items.AddRange(lines);
        var overflow = _items.Count - _capacity;
        if (overflow > 0)
        {
            _items.RemoveRange(0, overflow);
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
        RaiseReset();
    }

    public LogLineViewModel? FindByLineNumber(long lineNumber) => _items.Find(l => l.LineNumber == lineNumber);

    private void RaiseReset()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public IEnumerator<LogLineViewModel> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
