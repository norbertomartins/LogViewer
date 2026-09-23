using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;
using LogViewer.Core.Tailing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace LogViewer.App.Tests.ViewModels;

public sealed class OpenContainerLogsViewModelTests
{
    [Fact]
    public async Task Refresh_ListsDockerContainers_ThenPodsNamespacesAndContainersForKubernetes()
    {
        var cli = Substitute.For<IContainerCli>();
        cli.ListTargetsAsync(ContainerRuntime.Docker, null, null, Arg.Any<CancellationToken>()).Returns(["shop-api-1", "shop-db-1"]);
        cli.ListTargetsAsync(ContainerRuntime.Kubernetes, "shop", "prod", Arg.Any<CancellationToken>()).Returns(["orders-abc"]);
        cli.ListNamespacesAsync("prod", Arg.Any<CancellationToken>()).Returns(["default", "shop"]);
        cli.ListPodContainersAsync("orders-abc", "shop", "prod", Arg.Any<CancellationToken>()).Returns(["app", "sidecar"]);
        var vm = new OpenContainerLogsViewModel(cli);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(["shop-api-1", "shop-db-1"], vm.Targets);
        Assert.False(vm.IsValid);

        vm.Context = "prod";
        vm.Namespace = "shop";
        vm.IsKubernetes = true; // triggers a refresh on its own
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(["orders-abc"], vm.Targets);
        Assert.Equal(["default", "shop"], vm.Namespaces);

        vm.Target = "orders-abc";
        await Task.Yield();
        Assert.Equal(["app", "sidecar"], vm.Containers);

        vm.Container = "sidecar";
        var request = vm.ToRequest();
        Assert.Equal(new ContainerLogRequest(ContainerRuntime.Kubernetes, "orders-abc", "shop", "prod", "sidecar", 500, false), request);
    }

    [Fact]
    public async Task Refresh_ShowsCliErrors_AndStillAllowsATypedName()
    {
        var cli = Substitute.For<IContainerCli>();
        cli.ListTargetsAsync(default, default, default, default)
            .ReturnsForAnyArgs<Task<IReadOnlyList<string>>>(_ => throw new ContainerCliException("'docker' was not found"));
        var vm = new OpenContainerLogsViewModel(cli);

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("'docker' was not found", vm.StatusMessage);

        vm.Target = "my-container";
        Assert.True(vm.IsValid);
    }

    [Fact]
    public void MainViewModel_OpensTheLogsCommandAsAProcessTail()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowOpenContainerLogsDialog().Returns(new ContainerLogRequest(ContainerRuntime.Docker, "shop-api-1", TailLines: 50));
        var (main, settings) = MainViewModelFactory.Create(dialogService: dialogs);

        main.OpenContainerLogsCommand.Execute(null);

        var doc = Assert.Single(main.Documents);
        Assert.Equal(TailSourceKind.Process, doc.Kind);
        var recent = Assert.Single(settings.RecentSources);
        Assert.Equal("docker", recent.ProcessFileName);
        Assert.Equal("logs -f --tail 50 shop-api-1", recent.ProcessArguments);
        main.Dispose();
    }
}
