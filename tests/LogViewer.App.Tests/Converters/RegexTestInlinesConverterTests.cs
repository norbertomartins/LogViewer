using System.Windows.Documents;
using LogViewer.App.Converters;

namespace LogViewer.App.Tests.Converters;

public sealed class RegexTestInlinesConverterTests
{
    private readonly RegexTestInlinesConverter _converter = new();

    [Fact]
    public void Convert_WithNoMatches_ReturnsNull()
    {
        var result = _converter.Convert(["plain line", "ERROR", false, false], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithAKeywordMatch_BoldsOnlyTheMatchedSubstring()
    {
        var result = (_converter.Convert(["an ERROR happened", "ERROR", false, false], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        Assert.Equal(["an ", "ERROR", " happened"], result.Select(r => r.Text).ToArray());
        Assert.Null(result[0].Background);
        Assert.NotNull(result[1].Background);
        Assert.Equal(System.Windows.FontWeights.Bold, result[1].FontWeight);
        Assert.Null(result[2].Background);
    }

    [Fact]
    public void Convert_WithARegexMatchingMultipleSpots_BoldsEachOne()
    {
        var result = (_converter.Convert(["cat and cat", @"cat", true, false], typeof(object), null, null!) as List<Inline>)!
            .Cast<Run>().ToList();

        Assert.Equal(["cat", " and ", "cat"], result.Select(r => r.Text).ToArray());
        Assert.NotNull(result[0].Background);
        Assert.Null(result[1].Background);
        Assert.NotNull(result[2].Background);
    }

    [Fact]
    public void Convert_IsCaseSensitiveWhenAsked()
    {
        var caseSensitive = _converter.Convert(["Error here", "error", false, true], typeof(object), null, null!);
        var caseInsensitive = _converter.Convert(["Error here", "error", false, false], typeof(object), null, null!);

        Assert.Null(caseSensitive);
        Assert.NotNull(caseInsensitive);
    }

    [Fact]
    public void Convert_WithWrongBindingShape_ReturnsNull()
    {
        var result = _converter.Convert(["only one value"], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(new object(), [], null, null!));
    }
}
