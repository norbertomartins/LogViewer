using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class TimestampQueryTests
{
    private static readonly DateTimeOffset Reference = new(2026, 9, 23, 14, 30, 0, TimeSpan.FromHours(1));

    [Theory]
    [InlineData("2026-09-20 08:15", 2026, 9, 20, 8, 15, 0)]
    [InlineData("2026-09-20T08:15:42", 2026, 9, 20, 8, 15, 42)]
    [InlineData("2026-09-20 08:15:42.5", 2026, 9, 20, 8, 15, 42)]
    [InlineData("2026-09-20", 2026, 9, 20, 0, 0, 0)]
    public void FullDateTime_IsReadInTheReferencesOffset(string input, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(TimestampQuery.TryParse(input, Reference, out var result));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), new DateTime(result.Year, result.Month, result.Day, result.Hour, result.Minute, result.Second));
        Assert.Equal(Reference.Offset, result.Offset);
    }

    [Fact]
    public void ExplicitOffset_IsKept()
    {
        Assert.True(TimestampQuery.TryParse("2026-09-20T08:15:00Z", Reference, out var result));
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 8, 15, 0, TimeSpan.Zero), result);
    }

    [Theory]
    [InlineData("09:05", 9, 5, 0, 0)]
    [InlineData("09:05:07", 9, 5, 7, 0)]
    [InlineData("09:05:07,250", 9, 5, 7, 250)]
    public void TimeOfDay_UsesTheReferenceDate(string input, int h, int m, int s, int ms)
    {
        Assert.True(TimestampQuery.TryParse(input, Reference, out var result));
        Assert.Equal(new DateTimeOffset(2026, 9, 23, h, m, s, ms, Reference.Offset), result);
    }

    [Theory]
    [InlineData("-5m", -300_000)]
    [InlineData("+30s", 30_000)]
    [InlineData("-1h30m", -5_400_000)]
    [InlineData("-250ms", -250)]
    [InlineData("-1d", -86_400_000)]
    public void Relative_OffsetsFromTheReference(string input, long expectedMs)
    {
        Assert.True(TimestampQuery.TryParse(input, Reference, out var result));
        Assert.Equal(Reference.AddMilliseconds(expectedMs), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("25:00")]
    [InlineData("12:61")]
    public void Garbage_IsRejected(string input) => Assert.False(TimestampQuery.TryParse(input, Reference, out _));

    [Fact]
    public void Relative_WithoutReference_IsRejected() => Assert.False(TimestampQuery.TryParse("-5m", null, out _));
}
