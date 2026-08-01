using System.Collections.Concurrent;
using LogViewer.Core.Tailing;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Tailing;

public sealed class FileTailSourceTests
{
    private static readonly TailSourceOptions FastPollOptions = new() { PollInterval = TimeSpan.FromMilliseconds(30) };

    [Fact]
    public void Start_OnExistingFile_DeliversInitialLines()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("line1\nline2\nline3\n");

        var received = new ConcurrentQueue<string>();
        using var source = new FileTailSource(fixture.FilePath, FastPollOptions);
        source.LinesRead += (_, e) => Enqueue(received, e.Lines);

        source.Start();

        Assert.True(WaitUntil(() => received.Count >= 3), "Expected the initial tail lines to be delivered.");
        Assert.Equal(["line1", "line2", "line3"], received.ToArray());
    }

    [Fact]
    public void AppendedLines_AreDeliveredAsTheyArrive()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("line1\n");

        var received = new ConcurrentQueue<string>();
        using var source = new FileTailSource(fixture.FilePath, FastPollOptions);
        source.LinesRead += (_, e) => Enqueue(received, e.Lines);
        source.Start();

        Assert.True(WaitUntil(() => received.Count >= 1));

        fixture.AppendText("line2\nline3\n");

        Assert.True(WaitUntil(() => received.Count >= 3), "Expected appended lines to be delivered.");
        Assert.Equal(["line1", "line2", "line3"], received.ToArray());
    }

    [Fact]
    public void Truncation_RaisesResetAndResumesFromNewContent()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("old1\nold2\n");

        var received = new ConcurrentQueue<string>();
        var resets = new ConcurrentQueue<TailResetReason>();
        using var source = new FileTailSource(fixture.FilePath, FastPollOptions);
        source.LinesRead += (_, e) => Enqueue(received, e.Lines);
        source.SourceReset += (_, e) => resets.Enqueue(e.Reason);
        source.Start();

        Assert.True(WaitUntil(() => received.Count >= 2));

        fixture.Truncate();
        fixture.AppendText("new1\n");

        Assert.True(WaitUntil(() => resets.Contains(TailResetReason.Truncated)), "Expected a Truncated reset.");
        Assert.True(WaitUntil(() => received.Contains("new1")), "Expected post-truncation content to be read.");
    }

    [Fact]
    public void RenameAndRecreate_RaisesRotatedReset()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("old1\n");

        var received = new ConcurrentQueue<string>();
        var resets = new ConcurrentQueue<TailResetReason>();
        using var source = new FileTailSource(fixture.FilePath, FastPollOptions);
        source.LinesRead += (_, e) => Enqueue(received, e.Lines);
        source.SourceReset += (_, e) => resets.Enqueue(e.Reason);
        source.Start();

        Assert.True(WaitUntil(() => received.Count >= 1));

        fixture.RenameAndRecreate("rotated1\n");

        Assert.True(WaitUntil(() => resets.Contains(TailResetReason.Rotated)), "Expected a Rotated reset.");
        Assert.True(WaitUntil(() => received.Contains("rotated1")), "Expected the newly created file's content to be read.");
    }

    [Fact]
    public void Start_OnMissingFile_WaitsThenReadsOnceCreated()
    {
        using var fixture = new TempFileFixture();
        // Deliberately do not create the file yet.

        var received = new ConcurrentQueue<string>();
        using var source = new FileTailSource(fixture.FilePath, FastPollOptions);
        source.LinesRead += (_, e) => Enqueue(received, e.Lines);
        source.Start();

        fixture.WriteAllText("first1\n");

        Assert.True(WaitUntil(() => received.Contains("first1")), "Expected content to be read once the file appears.");
    }

    private static void Enqueue(ConcurrentQueue<string> queue, IReadOnlyList<TailLine> lines)
    {
        foreach (var line in lines)
        {
            queue.Enqueue(line.Text);
        }
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }
}
