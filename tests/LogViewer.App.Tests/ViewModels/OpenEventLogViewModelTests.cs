using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class OpenEventLogViewModelTests
{
    [Fact]
    public void Constructor_DefaultsToApplicationChannelWithNoFilters()
    {
        var viewModel = new OpenEventLogViewModel();

        Assert.Equal("Application", viewModel.ChannelName);
        Assert.Empty(viewModel.Filters);
        Assert.True(viewModel.IsValid);
    }

    [Fact]
    public void AddFilter_AppendsANewFilterAndSelectsIt()
    {
        var viewModel = new OpenEventLogViewModel();

        viewModel.AddFilterCommand.Execute(null);

        Assert.Single(viewModel.Filters);
        Assert.Same(viewModel.Filters[0], viewModel.SelectedFilter);
    }

    [Fact]
    public void RemoveFilter_WithExplicitFilter_RemovesItAndSelectsNextRemaining()
    {
        var viewModel = new OpenEventLogViewModel();
        viewModel.AddFilterCommand.Execute(null);
        viewModel.AddFilterCommand.Execute(null);
        var first = viewModel.Filters[0];
        var second = viewModel.Filters[1];

        viewModel.RemoveFilterCommand.Execute(first);

        Assert.Single(viewModel.Filters);
        Assert.Same(second, viewModel.Filters[0]);
        Assert.Same(second, viewModel.SelectedFilter);
    }

    [Fact]
    public void RemoveFilter_WithNullArgument_FallsBackToSelectedFilter()
    {
        var viewModel = new OpenEventLogViewModel();
        viewModel.AddFilterCommand.Execute(null);

        viewModel.RemoveFilterCommand.Execute(null);

        Assert.Empty(viewModel.Filters);
        Assert.Null(viewModel.SelectedFilter);
    }

    [Fact]
    public void IsValid_IsFalseWhenChannelNameIsBlank()
    {
        var viewModel = new OpenEventLogViewModel { ChannelName = "   " };

        Assert.False(viewModel.IsValid);
    }
}
