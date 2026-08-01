using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class RingLineBufferTests
{
    [Fact]
    public void Append_BelowCapacity_KeepsAllLinesInOrder()
    {
        var buffer = new RingLineBuffer(capacity: 5);

        for (var i = 1; i <= 3; i++)
        {
            buffer.Append(Line(i));
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal([1L, 2L, 3L], buffer.Select(l => l.LineNumber));
    }

    [Fact]
    public void Append_BeyondCapacity_EvictsOldestFirst()
    {
        var buffer = new RingLineBuffer(capacity: 3);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Append(Line(i));
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal([3L, 4L, 5L], buffer.Select(l => l.LineNumber));
    }

    [Fact]
    public void TotalLinesAppended_TracksAllAppendsEvenAfterEviction()
    {
        var buffer = new RingLineBuffer(capacity: 2);

        for (var i = 1; i <= 5; i++)
        {
            buffer.Append(Line(i));
        }

        Assert.Equal(5, buffer.TotalLinesAppended);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void Clear_ResetsCountAndTotal()
    {
        var buffer = new RingLineBuffer(capacity: 4);
        buffer.Append(Line(1));
        buffer.Append(Line(2));

        buffer.Clear();

        Assert.Empty(buffer);
        Assert.Equal(0, buffer.TotalLinesAppended);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var buffer = new RingLineBuffer(capacity: 2);
        buffer.Append(Line(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[1]);
    }

    private static TailLine Line(long number) => new(number, 0, $"line{number}", DateTimeOffset.UtcNow);
}
