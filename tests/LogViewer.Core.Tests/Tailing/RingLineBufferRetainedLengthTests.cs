using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class RingLineBufferRetainedLengthTests
{
    private static TailLine Line(long n, string text) => new(n, n, text, DateTimeOffset.UnixEpoch);

    [Fact]
    public void RetainedTextLength_TracksAppendsAndEvictions()
    {
        var buffer = new RingLineBuffer(capacity: 2);

        buffer.Append(Line(1, "abc"));   // 3
        buffer.Append(Line(2, "de"));    // 5
        Assert.Equal(5, buffer.RetainedTextLength);

        buffer.Append(Line(3, "fghij")); // evicts "abc" (-3), adds 5 => 7
        Assert.Equal(7, buffer.RetainedTextLength);
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void Clear_ResetsRetainedTextLength()
    {
        var buffer = new RingLineBuffer(capacity: 4);
        buffer.AppendRange([Line(1, "aaaa"), Line(2, "bbbb")]);
        Assert.Equal(8, buffer.RetainedTextLength);

        buffer.Clear();
        Assert.Equal(0, buffer.RetainedTextLength);
    }
}
