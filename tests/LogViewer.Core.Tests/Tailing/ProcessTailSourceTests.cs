using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class ProcessTailSourceTests
{
    private static ProcessTailOptions Cmd(string args, bool restart = false) => new()
    {
        FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
        Arguments = OperatingSystem.IsWindows() ? $"/c {args}" : $"-c \"{args}\"",
        RestartOnExit = restart,
        FlushInterval = TimeSpan.FromMilliseconds(40),
    };

    private static async Task<List<string>> Collect(ProcessTailSource source, Func<List<string>, bool> until, TimeSpan timeout)
    {
        var lines = new List<string>();
        source.LinesRead += (_, e) =>
        {
            lock (lines)
            {
                lines.AddRange(e.Lines.Select(l => l.Text));
            }
        };

        source.Start();
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            lock (lines)
            {
                if (until(lines))
                {
                    break;
                }
            }

            await Task.Delay(25);
        }

        source.Stop();
        return lines;
    }

    [Fact]
    public async Task EmitsProcessStdoutLines()
    {
        var args = OperatingSystem.IsWindows() ? "\"echo alpha& echo beta\"" : "'printf \"alpha\\nbeta\\n\"'";
        using var source = new ProcessTailSource(Cmd(args));

        var lines = await Collect(source, l => l.Count >= 2, TimeSpan.FromSeconds(8));

        Assert.Contains("alpha", lines);
        Assert.Contains("beta", lines);
    }

    [Fact]
    public async Task NonZeroExit_WithoutRestart_RaisesError()
    {
        var args = OperatingSystem.IsWindows() ? "\"exit 3\"" : "'exit 3'";
        using var source = new ProcessTailSource(Cmd(args));

        Exception? error = null;
        source.Error += (_, e) => error = e.Exception;
        source.Start();

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && error is null)
        {
            await Task.Delay(25);
        }

        source.Stop();
        Assert.NotNull(error);
        Assert.Contains("code 3", error!.Message);
    }

    [Fact]
    public async Task BadExecutable_RaisesError()
    {
        using var source = new ProcessTailSource(new ProcessTailOptions
        {
            FileName = "this-command-does-not-exist-xyz",
            RestartOnExit = false,
        });

        Exception? error = null;
        source.Error += (_, e) => error = e.Exception;
        source.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && error is null)
        {
            await Task.Delay(25);
        }

        source.Stop();
        Assert.NotNull(error);
    }
}
