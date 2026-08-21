using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class OpenDirectoryWatchViewModelTests
{
    [Fact]
    public void Constructor_WithoutInitialDirectory_DefaultsToEmptyPathAndLogPattern()
    {
        var viewModel = new OpenDirectoryWatchViewModel();

        Assert.Equal(string.Empty, viewModel.DirectoryPath);
        Assert.Equal("*.log", viewModel.Pattern);
        Assert.True(viewModel.AutoSwitchToLatestFile);
    }

    [Fact]
    public void Constructor_WithInitialDirectory_PrefillsDirectoryPath()
    {
        var viewModel = new OpenDirectoryWatchViewModel(@"C:\logs");

        Assert.Equal(@"C:\logs", viewModel.DirectoryPath);
    }

    [Theory]
    [InlineData("", "*.log", false)]
    [InlineData(" ", "*.log", false)]
    [InlineData(@"C:\logs", "", false)]
    [InlineData(@"C:\logs", " ", false)]
    [InlineData(@"C:\logs", "*.log", true)]
    public void IsValid_RequiresNonBlankDirectoryAndPattern(string directory, string pattern, bool expected)
    {
        var viewModel = new OpenDirectoryWatchViewModel { DirectoryPath = directory, Pattern = pattern };

        Assert.Equal(expected, viewModel.IsValid);
    }
}
