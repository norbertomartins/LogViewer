using System.Windows;
using System.Windows.Documents;
using LogViewer.App.Converters;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.Tests.Converters;

public sealed class HighlightSpanInlinesConverterTests
{
    private readonly HighlightSpanInlinesConverter _converter = new();

    [Fact]
    public void Convert_WithSpansDisabled_ReturnsNull()
    {
        var result = _converter.Convert(["an ERROR here", Spans((3, 5)), false], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithNoSpans_ReturnsNull()
    {
        var result = _converter.Convert(["plain text", Spans(), true], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithEmptyText_ReturnsNull()
    {
        var result = _converter.Convert(["", Spans((0, 1)), true], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithOneSpan_BoldsAndUnderlinesOnlyThatSubstring()
    {
        var result = (_converter.Convert(["an ERROR here", Spans((3, 5)), true], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        Assert.Equal(["an ", "ERROR", " here"], result.Select(r => r.Text).ToArray());
        Assert.Equal(FontWeights.Normal, result[0].FontWeight);
        Assert.Equal(FontWeights.Bold, result[1].FontWeight);
        Assert.Equal(TextDecorations.Underline, result[1].TextDecorations);
        Assert.Equal(FontWeights.Normal, result[2].FontWeight);
    }

    [Fact]
    public void Convert_WithMultipleNonOverlappingSpans_OrdersThemByPosition()
    {
        // Spans passed out of position order — the converter must sort by Start before walking the text.
        var result = (_converter.Convert(["one two three", Spans((8, 3), (0, 3)), true], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        Assert.Equal(["one", " two ", "thr", "ee"], result.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void Convert_WithOverlappingSpans_ClipsTheSecondSpanToWhereTheFirstEnded()
    {
        var result = (_converter.Convert(["abcdef", Spans((0, 4), (2, 4)), true], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        // (0,4) covers "abcd"; (2,4) would cover "cdef" but "cd" is already consumed, so only "ef" remains.
        Assert.Equal(["abcd", "ef"], result.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void Convert_WithASpanPastTheEndOfTheText_ClampsToTheTextsLength()
    {
        var result = (_converter.Convert(["short", Spans((2, 100)), true], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        Assert.Equal(["sh", "ort"], result.Select(r => r.Text).ToArray());
    }

    [Fact]
    public void Convert_DropsSpansEntirelyOutOfBoundsOrWithNonPositiveLength()
    {
        // Start == text.Length (out of bounds) and a zero-length span are both discarded, leaving no
        // valid spans at all — same as passing none.
        var result = _converter.Convert(["hello", Spans((5, 1), (1, 0)), true], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithWrongBindingShape_ReturnsNull()
    {
        var result = _converter.Convert(["hello", "not a span list", true], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(new object(), [], null, null!));
    }

    private static IReadOnlyList<HighlightSpan> Spans(params (int Start, int Length)[] spans) =>
        spans.Select(s => new HighlightSpan(s.Start, s.Length)).ToList();
}
