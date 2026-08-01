using System.Runtime.Versioning;
using LogViewer.Core.EventLogging;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.EventLogging;

/// <summary>
/// These tests exercise the real Windows Application event log channel, which reliably has existing
/// entries on any Windows machine — there is no practical way to unit-test EventLogWatcher against a
/// fake channel, so this is closer to an integration test scoped to Core.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsEventLogSourceTests
{
    [Fact]
    public void Start_OnApplicationChannel_DeliversInitialEntriesWithoutAdminRights()
    {
        var received = new List<string>();
        using var source = new WindowsEventLogSource("Application", options: new TailSourceOptions { InitialTailLineCount = 5 });
        source.LinesRead += (_, e) =>
        {
            foreach (var line in e.Lines)
            {
                received.Add(line.Text);
            }
        };

        Exception? observedError = null;
        source.Error += (_, e) => observedError = e.Exception;

        source.Start();

        Assert.True(WaitUntil(() => received.Count > 0 || observedError is not null), "Expected either initial entries or an error within the timeout.");
        Assert.Null(observedError);
        Assert.NotEmpty(received);
    }

    [Fact]
    public void Start_OnUnknownChannel_RaisesError()
    {
        Exception? observedError = null;
        using var source = new WindowsEventLogSource("LogViewer-Nonexistent-Channel-" + Guid.NewGuid());
        source.Error += (_, e) => observedError = e.Exception;

        source.Start();

        Assert.True(WaitUntil(() => observedError is not null));
        Assert.NotNull(observedError);
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }
}
