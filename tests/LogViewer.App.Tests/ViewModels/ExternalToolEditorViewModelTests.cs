using LogViewer.App.ViewModels;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.Tests.ViewModels;

public sealed class ExternalToolEditorViewModelTests
{
    [Fact]
    public void Constructor_WrapsEachToolAndSelectsTheFirst()
    {
        var tools = new[] { ExternalToolDefinition.CreateDefault("Notepad"), ExternalToolDefinition.CreateDefault("VS Code") };

        var viewModel = new ExternalToolEditorViewModel(tools, []);

        Assert.Equal(2, viewModel.Tools.Count);
        Assert.Equal("Notepad", viewModel.SelectedTool?.Name);
    }

    [Fact]
    public void Constructor_ProjectsAvailableHighlightRulesToNameOptions()
    {
        var rule = HighlightRule.CreateDefault("Error", "ERROR");

        var viewModel = new ExternalToolEditorViewModel([], [rule]);

        var option = Assert.Single(viewModel.AvailableHighlightRules);
        Assert.Equal(rule.Id, option.Id);
        Assert.Equal("Error", option.Name);
    }

    [Fact]
    public void AddTool_AppendsANewToolAndSelectsIt()
    {
        var viewModel = new ExternalToolEditorViewModel([], []);

        viewModel.AddToolCommand.Execute(null);

        var added = Assert.Single(viewModel.Tools);
        Assert.Same(added, viewModel.SelectedTool);
    }

    [Fact]
    public void RemoveTool_WithAnExplicitParameter_RemovesThatToolAndReselectsTheFirstRemaining()
    {
        var tools = new[] { ExternalToolDefinition.CreateDefault("A"), ExternalToolDefinition.CreateDefault("B") };
        var viewModel = new ExternalToolEditorViewModel(tools, []);
        var toRemove = viewModel.Tools[0];

        viewModel.RemoveToolCommand.Execute(toRemove);

        var remaining = Assert.Single(viewModel.Tools);
        Assert.Equal("B", remaining.Name);
        Assert.Same(remaining, viewModel.SelectedTool);
    }

    [Fact]
    public void RemoveTool_WithNoParameter_FallsBackToRemovingTheSelectedTool()
    {
        var tools = new[] { ExternalToolDefinition.CreateDefault("A"), ExternalToolDefinition.CreateDefault("B") };
        var viewModel = new ExternalToolEditorViewModel(tools, []);

        viewModel.RemoveToolCommand.Execute(null);

        Assert.Single(viewModel.Tools);
        Assert.Equal("B", viewModel.Tools[0].Name);
    }

    [Fact]
    public void RemoveTool_WhenNothingIsSelectedOrPassed_DoesNothing()
    {
        var viewModel = new ExternalToolEditorViewModel([], []) { SelectedTool = null };

        viewModel.RemoveToolCommand.Execute(null);

        Assert.Empty(viewModel.Tools);
    }

    [Fact]
    public void ToDefinitions_RoundTripsEveryToolInOrder()
    {
        var tools = new[] { ExternalToolDefinition.CreateDefault("A"), ExternalToolDefinition.CreateDefault("B") };
        var viewModel = new ExternalToolEditorViewModel(tools, []);

        var result = viewModel.ToDefinitions();

        Assert.Equal(["A", "B"], result.Select(d => d.Name).ToArray());
    }
}
