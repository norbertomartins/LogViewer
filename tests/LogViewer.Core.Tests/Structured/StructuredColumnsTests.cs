using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class StructuredColumnsTests
{
    private static StructuredLogEvent Event(string level, params (string Key, string Value)[] props) =>
        new(null, level, null, "msg", null, props.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void DiscoverProperties_CountsEachNameMostCommonFirst()
    {
        var usage = StructuredColumns.DiscoverProperties(
        [
            Event("Information", ("RequestPath", "/a"), ("Elapsed", "12")),
            Event("Error", ("RequestPath", "/b")),
            Event("Information", ("app", "shop"), ("RequestPath", "/c")),
        ]);

        Assert.Equal(
            [new StructuredPropertyUsage("RequestPath", 3), new StructuredPropertyUsage("app", 1), new StructuredPropertyUsage("Elapsed", 1)],
            usage);
    }

    [Fact]
    public void CompareValues_SortsNumbersNumericallyThenTextThenMissing()
    {
        string?[] values = ["b", null, "100", "20", "A", "3.5"];

        var sorted = values.Order(Comparer<string?>.Create(StructuredColumns.CompareValues)).ToArray();

        Assert.Equal((IEnumerable<string?>)["3.5", "20", "100", "A", "b", null], sorted);
    }

    [Fact]
    public void ValueFilter_MatchesPropertiesAndPseudoFields_AndCanExclude()
    {
        var error = Event("Error", ("RequestPath", "/b"));
        var info = Event("Information", ("RequestPath", "/a"));

        Assert.True(new StructuredValueFilter("RequestPath", "/b").Matches(error));
        Assert.False(new StructuredValueFilter("RequestPath", "/b").Matches(info));
        Assert.False(new StructuredValueFilter(StructuredFieldResolver.LevelField, "Error", Exclude: true).Matches(error));
        Assert.True(new StructuredValueFilter(StructuredFieldResolver.LevelField, "Error", Exclude: true).Matches(info));

        // A missing property filters as a null value.
        Assert.True(new StructuredValueFilter("Missing", null).Matches(info));
    }
}
