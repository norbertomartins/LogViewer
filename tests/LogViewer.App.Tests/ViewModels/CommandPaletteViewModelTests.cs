using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class CommandPaletteViewModelTests
{
    private static PaletteCommand Cmd(string title, string category = "Test") => new(title, category, () => { });

    [Fact]
    public void EmptyQuery_ReturnsEverythingInOrder()
    {
        var all = new[] { Cmd("Alpha"), Cmd("Beta"), Cmd("Gamma") };
        var vm = new CommandPaletteViewModel(all);

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, vm.Results.Select(r => r.Title));
        Assert.Same(all[0], vm.Selected);
    }

    [Fact]
    public void Query_PrefersTitlePrefixThenSubstringThenFuzzy()
    {
        var all = new[]
        {
            Cmd("Reopen last file"),          // fuzzy: r..e..open? contains "open" substring actually
            Cmd("Open Settings"),             // prefix match on "Open"
            Cmd("Toggle Follow", "Open doc"), // category substring
        };
        var vm = new CommandPaletteViewModel(all) { Query = "open" };

        Assert.Equal("Open Settings", vm.Results[0].Title);
    }

    [Fact]
    public void Query_DropsNonMatches()
    {
        var vm = new CommandPaletteViewModel(new[] { Cmd("Export"), Cmd("Search") }) { Query = "zzz" };
        Assert.Empty(vm.Results);
        Assert.Null(vm.Selected);
    }

    [Fact]
    public void MoveSelection_ClampsWithinResults()
    {
        var vm = new CommandPaletteViewModel(new[] { Cmd("One"), Cmd("Two") });
        vm.MoveSelection(-1);
        Assert.Equal("One", vm.Selected!.Title);
        vm.MoveSelection(1);
        Assert.Equal("Two", vm.Selected!.Title);
        vm.MoveSelection(5);
        Assert.Equal("Two", vm.Selected!.Title);
    }

    [Fact]
    public void FuzzySubsequence_Matches()
    {
        var vm = new CommandPaletteViewModel(new[] { Cmd("Open Windows Event Log") }) { Query = "owel" };
        Assert.Single(vm.Results);
    }
}
