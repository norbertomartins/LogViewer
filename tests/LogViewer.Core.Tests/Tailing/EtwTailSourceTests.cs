using LogViewer.Core.Tailing;
using Microsoft.Diagnostics.Tracing.Session;

namespace LogViewer.Core.Tests.Tailing;

public sealed class EtwTailSourceTests
{
    [Fact]
    public void DisplayName_IncludesProvider()
    {
        var source = new EtwTailSource(new EtwTailOptions { Provider = "Microsoft-Windows-DotNETRuntime" });
        Assert.Equal("[ETW] Microsoft-Windows-DotNETRuntime", source.DisplayName);
    }

    [Fact]
    public async Task Start_WhenNotElevated_RaisesAdministratorError()
    {
        if (!OperatingSystem.IsWindows() || TraceEventSession.IsElevated().GetValueOrDefault())
        {
            return; // only meaningful for a non-elevated Windows run
        }

        using var source = new EtwTailSource(new EtwTailOptions { Provider = "Microsoft-Windows-Kernel-Process" });

        Exception? error = null;
        source.Error += (_, e) => error = e.Exception;
        source.Start();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && error is null)
        {
            await Task.Delay(25);
        }

        source.Stop();
        Assert.IsType<UnauthorizedAccessException>(error);
    }
}
