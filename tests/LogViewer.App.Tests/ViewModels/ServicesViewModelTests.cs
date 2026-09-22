using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

/// <summary>
/// <see cref="ServicesViewModel"/> constructs its own <c>ServiceControlService</c> internally (no DI
/// seam to substitute), so these tests exercise it against the real, local Windows Service Control
/// Manager — read-only operations only. Starting/stopping a real service is deliberately not covered
/// here: it would mutate actual machine state, which a unit test must never do.
/// </summary>
public sealed class ServicesViewModelTests
{
    [Fact]
    public void Constructor_ListsRealWindowsServices()
    {
        var viewModel = new ServicesViewModel();

        // Every Windows machine has a nonzero number of registered services.
        Assert.NotEmpty(viewModel.Services);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void Constructor_SetsAStatusMessageReflectingTheListedCount()
    {
        var viewModel = new ServicesViewModel();

        Assert.Contains(viewModel.Services.Count.ToString(), viewModel.StatusMessage);
    }

    [Fact]
    public void Refresh_WithASelectedService_KeepsItSelectedIfStillPresent()
    {
        var viewModel = new ServicesViewModel();
        var first = viewModel.Services[0];
        viewModel.SelectedService = first;

        viewModel.RefreshCommand.Execute(null);

        Assert.Equal(first.ServiceName, viewModel.SelectedService?.ServiceName);
    }

    [Fact]
    public async Task StartServiceAsync_WithNoSelection_CompletesWithoutTouchingAnything()
    {
        var viewModel = new ServicesViewModel { SelectedService = null };

        await viewModel.StartServiceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task StopServiceAsync_WithNoSelection_CompletesWithoutTouchingAnything()
    {
        var viewModel = new ServicesViewModel { SelectedService = null };

        await viewModel.StopServiceCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsBusy);
    }
}
