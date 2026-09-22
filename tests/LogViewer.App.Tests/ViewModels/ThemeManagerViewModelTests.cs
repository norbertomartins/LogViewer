using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;
using LogViewer.Core.Theming;

namespace LogViewer.App.Tests.ViewModels;

public sealed class ThemeManagerViewModelTests
{
    [Fact]
    public void Constructor_ListsBuiltInThemesPlusCustomOnes_AndSelectsTheActiveOne()
    {
        var custom = BuiltInThemes.Dark.Duplicate("My Theme");
        var settings = new AppSettings { ActiveThemeId = custom.Id, CustomThemes = [custom] };

        var viewModel = new ThemeManagerViewModel(settings);

        Assert.Equal(3, viewModel.Themes.Count);
        Assert.Equal(custom.Id, viewModel.SelectedTheme?.Id);
        Assert.True(viewModel.SelectedTheme!.IsActive);
        Assert.True(viewModel.Themes.Single(t => t.Id == BuiltInThemes.LightId) is { IsActive: false, IsBuiltIn: true });
    }

    [Fact]
    public void Constructor_WithAnUnknownActiveThemeId_FallsBackToTheFirstTheme()
    {
        var settings = new AppSettings { ActiveThemeId = "deleted-theme" };

        var viewModel = new ThemeManagerViewModel(settings);

        Assert.Equal(BuiltInThemes.LightId, viewModel.SelectedTheme?.Id);
    }

    [Fact]
    public void NewTheme_DuplicatesTheSelectedThemeAsABasis_AndSelectsIt()
    {
        var settings = new AppSettings { ActiveThemeId = BuiltInThemes.DarkId };
        var viewModel = new ThemeManagerViewModel(settings);
        viewModel.SelectedTheme = viewModel.Themes.Single(t => t.Id == BuiltInThemes.DarkId);

        viewModel.NewThemeCommand.Execute(null);

        Assert.Equal(3, viewModel.Themes.Count);
        Assert.False(viewModel.SelectedTheme!.IsBuiltIn);
        Assert.Equal("New Theme", viewModel.SelectedTheme.Name);
        // The new theme copies the selected (Dark) theme's colors as its starting point.
        Assert.Equal(ThemeBaseMode.Dark, viewModel.SelectedTheme.ToAppTheme().BaseMode);
    }

    [Fact]
    public void Duplicate_CopiesTheSelectedThemeWithACopySuffix()
    {
        var viewModel = new ThemeManagerViewModel(new AppSettings());
        viewModel.SelectedTheme = viewModel.Themes.Single(t => t.Id == BuiltInThemes.LightId);

        viewModel.DuplicateCommand.Execute(null);

        Assert.Equal("Light Copy", viewModel.SelectedTheme!.Name);
        Assert.False(viewModel.SelectedTheme.IsBuiltIn);
    }

    [Fact]
    public void Delete_OnABuiltInTheme_IsANoOp()
    {
        var viewModel = new ThemeManagerViewModel(new AppSettings());
        viewModel.SelectedTheme = viewModel.Themes.Single(t => t.Id == BuiltInThemes.LightId);
        var countBefore = viewModel.Themes.Count;

        viewModel.DeleteCommand.Execute(null);

        Assert.Equal(countBefore, viewModel.Themes.Count);
    }

    [Fact]
    public void Delete_OnTheActiveCustomTheme_FallsBackToLight()
    {
        var custom = BuiltInThemes.Dark.Duplicate("My Theme");
        var settings = new AppSettings { ActiveThemeId = custom.Id, CustomThemes = [custom] };
        var viewModel = new ThemeManagerViewModel(settings);
        viewModel.SelectedTheme = viewModel.Themes.Single(t => t.Id == custom.Id);

        viewModel.DeleteCommand.Execute(null);

        Assert.DoesNotContain(viewModel.Themes, t => t.Id == custom.Id);
        Assert.Equal(BuiltInThemes.LightId, viewModel.ActiveThemeId);
        Assert.True(viewModel.Themes.Single(t => t.Id == BuiltInThemes.LightId).IsActive);
    }

    [Fact]
    public void UseTheme_MarksOnlyThatThemeAsActive()
    {
        var viewModel = new ThemeManagerViewModel(new AppSettings());
        var dark = viewModel.Themes.Single(t => t.Id == BuiltInThemes.DarkId);

        viewModel.UseThemeCommand.Execute(dark);

        Assert.Equal(BuiltInThemes.DarkId, viewModel.ActiveThemeId);
        Assert.True(dark.IsActive);
        Assert.False(viewModel.Themes.Single(t => t.Id == BuiltInThemes.LightId).IsActive);
    }

    [Fact]
    public void ToCustomThemes_ExcludesBuiltInThemes()
    {
        var custom = BuiltInThemes.Light.Duplicate("Mine");
        var settings = new AppSettings { CustomThemes = [custom] };
        var viewModel = new ThemeManagerViewModel(settings);

        var result = viewModel.ToCustomThemes();

        var only = Assert.Single(result);
        Assert.Equal(custom.Id, only.Id);
    }
}
