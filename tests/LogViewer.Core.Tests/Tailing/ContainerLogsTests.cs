using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class ContainerLogsTests
{
    [Fact]
    public void Docker_FollowsTheContainerWithTail()
    {
        var (file, args) = ContainerLogs.BuildCommand(new ContainerLogRequest(ContainerRuntime.Docker, "shop-api-1", TailLines: 200, Timestamps: true));

        Assert.Equal("docker", file);
        Assert.Equal("logs -f --tail 200 --timestamps shop-api-1", args);
    }

    [Fact]
    public void Kubernetes_AddsContextNamespaceAndContainer()
    {
        var (file, args) = ContainerLogs.BuildCommand(
            new ContainerLogRequest(ContainerRuntime.Kubernetes, "orders-7d9f-abc", "shop", "prod-eu", "sidecar", 100));

        Assert.Equal("kubectl", file);
        Assert.Equal("logs -f --tail=100 --context prod-eu -n shop orders-7d9f-abc -c sidecar", args);
    }

    [Theory]
    [InlineData("pod; rm -rf /")]
    [InlineData("--all-namespaces")]
    [InlineData("a b")]
    [InlineData("")]
    public void RejectsNamesThatCouldInjectArguments(string target) =>
        Assert.Throws<ArgumentException>(() => ContainerLogs.BuildCommand(new ContainerLogRequest(ContainerRuntime.Docker, target)));

    [Fact]
    public void ParseNames_StripsKindPrefixes_DropsUnsafeNames_AndSorts()
    {
        var names = ContainerLogs.ParseNames("pod/web-2\r\npod/web-1\n\npod/bad?name\npod/web-1\n", "pod/");

        Assert.Equal(["web-1", "web-2"], names);
    }
}
