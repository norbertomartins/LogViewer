using System.Collections.Concurrent;
using System.Windows.Threading;
using LogViewer.Core.Tailing;

namespace LogViewer.App.Services;

/// <summary>
/// Bridges a background-thread <see cref="ITailSource"/> to the UI thread: queues incoming line
/// batches and reset notifications, then drains them on a single throttled dispatcher tick instead
/// of reacting to every event individually. This is what keeps the UI thread responsive at &gt;100
/// lines/sec — at most one consolidated update per tick, in arrival order.
/// </summary>
public sealed class UiDispatcherLineSink : IDisposable
{
    private readonly record struct QueueItem(IReadOnlyList<TailLine>? Lines, TailResetReason? Reset);

    private readonly ConcurrentQueue<QueueItem> _queue = new();
    private readonly DispatcherTimer _timer;

    public event Action<IReadOnlyList<TailLine>>? LinesFlushed;
    public event Action<TailResetReason>? ResetFlushed;

    public UiDispatcherLineSink(TimeSpan interval)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = interval };
        _timer.Tick += (_, _) => Flush();
        _timer.Start();
    }

    public void EnqueueLines(IReadOnlyList<TailLine> lines) => _queue.Enqueue(new QueueItem(lines, null));

    public void EnqueueReset(TailResetReason reason) => _queue.Enqueue(new QueueItem(null, reason));

    private void Flush()
    {
        if (_queue.IsEmpty)
        {
            return;
        }

        List<TailLine>? pendingLines = null;
        while (_queue.TryDequeue(out var item))
        {
            if (item.Reset is { } reason)
            {
                FlushPendingLines(ref pendingLines);
                ResetFlushed?.Invoke(reason);
            }
            else if (item.Lines is not null)
            {
                (pendingLines ??= new List<TailLine>()).AddRange(item.Lines);
            }
        }

        FlushPendingLines(ref pendingLines);
    }

    private void FlushPendingLines(ref List<TailLine>? pendingLines)
    {
        if (pendingLines is { Count: > 0 })
        {
            LinesFlushed?.Invoke(pendingLines);
        }

        pendingLines = null;
    }

    public void Dispose() => _timer.Stop();
}
