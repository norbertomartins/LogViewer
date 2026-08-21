using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class DocumentCustomizeViewModelTests
{
    [Fact]
    public void Constructor_PrefillsSelectedColorAndGlyphFromCurrentValues()
    {
        var viewModel = new DocumentCustomizeViewModel("#3366CC", "⭐");

        Assert.Equal("#3366CC", viewModel.SelectedColorHex);
        Assert.Equal("⭐", viewModel.SelectedIconGlyph);
    }

    [Fact]
    public void Constructor_WithNullCurrentValues_LeavesSelectionsNull()
    {
        var viewModel = new DocumentCustomizeViewModel(null, null);

        Assert.Null(viewModel.SelectedColorHex);
        Assert.Null(viewModel.SelectedIconGlyph);
    }

    [Fact]
    public void AvailableColors_StartsWithNullForNoColorOption()
    {
        var viewModel = new DocumentCustomizeViewModel(null, null);

        Assert.Null(viewModel.AvailableColors[0]);
        Assert.Contains("#3366CC", viewModel.AvailableColors);
    }

    [Fact]
    public void AvailableGlyphs_StartsWithNullForNoGlyphOption()
    {
        var viewModel = new DocumentCustomizeViewModel(null, null);

        Assert.Null(viewModel.AvailableGlyphs[0]);
        Assert.Contains("⭐", viewModel.AvailableGlyphs);
    }
}
