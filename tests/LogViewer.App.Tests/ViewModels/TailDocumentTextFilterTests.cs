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
