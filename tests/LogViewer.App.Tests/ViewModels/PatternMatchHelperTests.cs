using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class PatternMatchHelperTests
{
    [Fact]
    public void Substring_FindsAllOccurrences_CaseInsensitiveByDefault()
    {
        var m = PatternMatchHelper.Matches("ERROR error Error", "error", isRegex: false, caseSensitive: false);
        Assert.Equal(3, m.Count);
        Assert.Equal((0, 5), m[0]);
    }

    [Fact]
    public void Substring_CaseSensitive_Respected()
    {
        var m = PatternMatchHelper.Matches("ERROR error", "error", isRegex: false, caseSensitive: true);
        Assert.Single(m);
        Assert.Equal((6, 5), m[0]);
    }

    [Fact]
    public void Regex_ReturnsMatchRanges()
    {
        var m = PatternMatchHelper.Matches("id=42 id=7", @"id=(\d+)", isRegex: true, caseSensitive: false);
        Assert.Equal(2, m.Count);
        Assert.Equal((0, 5), m[0]);
    }

    [Fact]
    public void Regex_Invalid_ReturnsEmpty_NotThrow()
    {
        Assert.Empty(PatternMatchHelper.Matches("abc", "(", isRegex: true, caseSensitive: false));
    }

    [Fact]
    public void HighlightRuleViewModel_TesterSummary_CountsMatchingLines()
    {
        var vm = new LogViewer.App.ViewModels.HighlightRuleViewModel
        {
            Pattern = "ERROR",
            IsRegex = false,
            TesterInput = "INFO ok\nERROR bad\nERROR worse",
        };

        Assert.Equal("2 / 3 lines match", vm.TesterSummary);
    }
}
