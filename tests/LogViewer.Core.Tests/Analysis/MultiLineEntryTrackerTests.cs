using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class MultiLineEntryTrackerTests
{
    /// <summary>Feeds the lines through a tracker and returns, per line, the 1-based number of the entry head it was
    /// grouped under (its own number when it is a head), applying any adopted headers the way the view does.</summary>
    private static long[] Heads(params string[] lines)
    {
        var tracker = new MultiLineEntryTracker<Box>();
        var boxes = lines.Select((text, i) => new Box(i + 1)).ToArray();
        var heads = boxes.Select(b => b.Number).ToArray();
        for (var i = 0; i < lines.Length; i++)
        {
            var placement = tracker.Add(boxes[i], lines[i]);
            if (placement.Head is { } head)
            {
                heads[i] = head.Number;
            }

            if (placement.AdoptedHeader is { } adopted)
            {
                heads[adopted.Number - 1] = placement.Head!.Number;
            }
        }

        return heads;
    }

    [Fact]
    public void DotNetFramesAndInnerExceptionsBelongToTheLineAbove()
    {
        var heads = Heads(
            "2026-01-01 10:00:00 ERROR Payment failed: System.TimeoutException: gateway",
            "   at Shop.Gateway.Charge() in C:\\src\\Gateway.cs:line 12",
            " ---> System.IO.IOException: reset",
            "   at Shop.Net.Read()",
            "   --- End of inner exception stack trace ---",
            "2026-01-01 10:00:01 INFO next");

        Assert.Equal([1, 1, 1, 1, 1, 6], heads);
    }

    [Fact]
    public void JavaCausedByAndMoreLinesStayInTheTrace()
    {
        var heads = Heads(
            "ERROR request failed",
            "java.lang.IllegalStateException: boom",
            "\tat com.shop.Api.handle(Api.java:42)",
            "Caused by: java.io.IOException: closed",
            "\tat com.shop.Io.read(Io.java:7)",
            "\t... 12 more",
            "INFO done");

        // The bare header on line 2 is adopted into line 1's entry once a frame follows it.
        Assert.Equal([1, 1, 1, 1, 1, 1, 7], heads);
    }

    [Fact]
    public void PythonTracebackEndsAtTheClosingErrorLine()
    {
        var heads = Heads(
            "ERROR:root:import failed",
            "Traceback (most recent call last):",
            "  File \"/app/main.py\", line 3, in <module>",
            "    run()",
            "ValueError: bad input",
            "INFO:root:retrying");

        Assert.Equal([1, 1, 1, 1, 1, 6], heads);
    }

    [Fact]
    public void ProseMentioningAnExceptionOrIndentedTextWithoutATraceStartsNewEntries()
    {
        var heads = Heads(
            "INFO retry after TimeoutException",
            "    indented but no trace started",
            "System.InvalidOperationException: no frames follow",
            "INFO next");

        Assert.Equal([1, 2, 3, 4], heads);
    }

    [Fact]
    public void ABlankLineEndsTheEntry()
    {
        var heads = Heads(
            "ERROR x",
            "   at A.B()",
            "",
            "   at C.D()");

        // After the blank line the orphan frame starts an entry of its own.
        Assert.Equal([1, 1, 3, 4], heads);
    }

    [Fact]
    public void StartEntry_ForcesAHeadAndFramesFollowIt()
    {
        var tracker = new MultiLineEntryTracker<Box>();
        var first = new Box(1);
        tracker.StartEntry(first, "{\"@l\":\"Error\"}");

        var placement = tracker.Add(new Box(2), "   at A.B()");

        Assert.Same(first, placement.Head);
        Assert.Null(placement.AdoptedHeader);
    }

    private sealed record Box(long Number);
}
