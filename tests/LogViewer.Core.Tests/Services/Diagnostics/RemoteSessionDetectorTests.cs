using LogViewer.Core.Services.Diagnostics;

namespace LogViewer.Core.Tests.Services.Diagnostics;

public sealed class RemoteSessionDetectorTests
{
    [Fact]
    public void EffectiveRefreshInterval_NotRemote_ReturnsConfiguredValueUnchanged()
    {
        var configured = TimeSpan.FromMilliseconds(100);

        var result = RemoteSessionDetector.EffectiveRefreshInterval(configured, isRemoteSession: false);

        Assert.Equal(configured, result);
    }

    [Fact]
    public void EffectiveRefreshInterval_Remote_WidensIntervalBelowMinimum()
    {
        var configured = TimeSpan.FromMilliseconds(100);

        var result = RemoteSessionDetector.EffectiveRefreshInterval(configured, isRemoteSession: true);

        Assert.Equal(TimeSpan.FromMilliseconds(250), result);
    }

    [Fact]
    public void EffectiveRefreshInterval_Remote_LeavesAlreadyWideIntervalUnchanged()
    {
        var configured = TimeSpan.FromMilliseconds(500);

        var result = RemoteSessionDetector.EffectiveRefreshInterval(configured, isRemoteSession: true);

        Assert.Equal(configured, result);
    }
}
