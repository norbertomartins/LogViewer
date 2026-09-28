using LogViewer.App.Models;
using LogViewer.App.Tests.TestUtilities;

namespace LogViewer.App.Tests.ViewModels;

public sealed class TailDocumentTextFilterTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void TextFilter_Regex_IncludeAndExclude()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\n"));

        Assert.True(doc.PassesTextFilter("anything")); // no filter

        doc.TextFilterPattern = @"ERROR|WARN";
        Assert.True(doc.IsTextFilterActive);
        Assert.True(doc.IsFilterActive);
        Assert.True(doc.PassesTextFilter("2026 ERROR boom"));
        Assert.False(doc.PassesTextFilter("2026 INFO ok"));

        doc.TextFilterExclude = true;
        Assert.False(doc.PassesTextFilter("2026 ERROR boom"));
        Assert.True(doc.PassesTextFilter("2026 INFO ok"));

        viewModel.Dispose();
    }

    [Fact]
    public void TextFilter_ForADisplayedLine_CachedResultIsInvalidatedWhenTheFilterChanges()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\n"));
        var line = new LogLineViewModel(1, "2026 ERROR Boom", null, null, isBookmarked: false);

        doc.TextFilterPattern = "ERROR";
        Assert.True(doc.PassesTextFilter(line));
        Assert.True(doc.PassesTextFilter(line)); // served from the per-line cache

        doc.TextFilterExclude = true; // applied on top of the cached match
        Assert.False(doc.PassesTextFilter(line));
        doc.TextFilterExclude = false;

        doc.TextFilterPattern = "WARN";
        Assert.False(doc.PassesTextFilter(line));

        doc.TextFilterIsRegex = false;
        doc.TextFilterPattern = "boom";
        Assert.True(doc.PassesTextFilter(line));

        doc.TextFilterCaseSensitive = true;
        Assert.False(doc.PassesTextFilter(line));

        doc.TextFilterPattern = null;
        Assert.True(doc.PassesTextFilter(line));

        viewModel.Dispose();
    }

    [Fact]
    public void TextFilter_RegexModeWithoutMetacharacters_MatchesAsLiteral_AndHonorsCase()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\n"));

        doc.TextFilterPattern = "payment failed";
        Assert.True(doc.PassesTextFilter("2026 Payment FAILED for order 7"));
        Assert.False(doc.PassesTextFilter("2026 payment ok"));

        doc.TextFilterCaseSensitive = true;
        Assert.False(doc.PassesTextFilter("2026 Payment FAILED for order 7"));
        Assert.True(doc.PassesTextFilter("2026 payment failed for order 7"));

        doc.TextFilterPattern = "order [0-9]+$"; // real regex again
        Assert.True(doc.PassesTextFilter("payment failed for order 7"));
        Assert.False(doc.PassesTextFilter("payment failed for order [0-9]+$"));

        viewModel.Dispose();
    }

    [Fact]
    public void TextFilter_PlainSubstring_CaseInsensitiveByDefault()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\n"));

        doc.TextFilterIsRegex = false;
        doc.TextFilterPattern = "timeout";
        Assert.True(doc.PassesTextFilter("Request TIMEOUT after 30s"));

        doc.TextFilterCaseSensitive = true;
        Assert.False(doc.PassesTextFilter("Request TIMEOUT after 30s"));
        Assert.True(doc.PassesTextFilter("Request timeout after 30s"));

        viewModel.Dispose();
    }

    [Fact]
    public void TextFilter_InvalidRegex_DoesNotHideEverything_AndReportsStatus()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("a.log", "one\n"));

        doc.TextFilterPattern = "([unclosed";

        Assert.True(doc.PassesTextFilter("any line"));
        Assert.Contains("Invalid filter regex", doc.StatusMessage);

        viewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
