using System.Windows.Threading;
using LogViewer.App.Services;
using LogViewer.Core.Tailing;

namespace LogViewer.App.Tests.Services;

public sealed class UiDispatcherLineSinkTests
{
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public void EnqueueLines_FlushesMultipleBatchesAsOneConsolidatedEvent()
    {
        using var sink = new UiDispatcherLineSink(FastInterval);
        var flushes = new List<IReadOnlyList<TailLine>>();
        sink.LinesFlushed += lines => flushes.Add(lines);

        sink.EnqueueLines([Line(1, "a"), Line(2, "b")]);
        sink.EnqueueLines([Line(3, "c")]);

        SpinUntil(() => flushes.Count > 0);

        Assert.Single(flushes);
        Assert.Equal(["a", "b", "c"], flushes[0].Select(l => l.Text).ToArray());
    }

    [Fact]
    public void EnqueueReset_FlushesPendingLinesBeforeRaisingReset()
    {
        using var sink = new UiDispatcherLineSink(FastInterval);
        var events = new List<string>();
        sink.LinesFlushed += _ => events.Add("lines");
        sink.ResetFlushed += _ => events.Add("reset");

        sink.EnqueueLines([Line(1, "before-reset")]);
        sink.EnqueueReset(TailResetReason.Truncated);
        sink.EnqueueLines([Line(1, "after-reset")]);

        SpinUntil(() => events.Count >= 3);

        // Pending lines queued before the reset must flush first, then the reset, then anything queued
        // after it — never combined with the pre-reset batch and never reordered around the reset.
        Assert.Equal(["lines", "reset", "lines"], events);
    }

    [Fact]
    public void Flush_WithNothingQueued_NeverRaisesEitherEvent()
    {
        using var sink = new UiDispatcherLineSink(FastInterval);
        var flushed = false;
        sink.LinesFlushed += _ => flushed = true;
        sink.ResetFlushed += _ => flushed = true;

        // Let several ticks pass with an empty queue.
        SpinFor(TimeSpan.FromMilliseconds(150));

        Assert.False(flushed);
        Assert.Equal(0, sink.LastFlushMilliseconds);
    }

    [Fact]
    public void Dispose_StopsFurtherFlushes()
    {
        using var sink = new UiDispatcherLineSink(FastInterval);
        var flushCount = 0;
        sink.LinesFlushed += _ => flushCount++;

        sink.EnqueueLines([Line(1, "before-dispose")]);
        SpinUntil(() => flushCount == 1);

        sink.Dispose();
        sink.EnqueueLines([Line(2, "after-dispose")]);
        SpinFor(TimeSpan.FromMilliseconds(150));

        Assert.Equal(1, flushCount);
    }

    private static TailLine Line(long number, string text) => new(number, 0, text, DateTimeOffset.UtcNow);

    private static void SpinUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    private static void SpinFor(TimeSpan duration)
    {
        var deadline = Environment.TickCount64 + (long)duration.TotalMilliseconds;
        while (Environment.TickCount64 < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }
}
