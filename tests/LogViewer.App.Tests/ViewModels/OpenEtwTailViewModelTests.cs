using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class OpenEtwTailViewModelTests
{
    [Fact]
    public void LevelOptions_IncludeDebug()
    {
        var vm = new OpenEtwTailViewModel();
        Assert.Contains("Debug", vm.LevelOptions);
    }

    [Theory]
    [InlineData("Critical", 1)]
    [InlineData("Informational", 4)]
    [InlineData("Verbose", 5)]
    [InlineData("Debug", 0xFF)]
    public void LevelValue_MapsSelectedNameToEtwByte(string level, int expected)
    {
        var vm = new OpenEtwTailViewModel { Level = level };
        Assert.Equal(expected, vm.LevelValue);
    }
}
