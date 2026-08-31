using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class SshTailSourceTests
{
    [Fact]
    public void DisplayName_IsUserHostAndCommand()
    {
        var source = new SshTailSource(new SshTailOptions
        {
            Host = "server.example",
            Username = "deploy",
            Password = "x",
            Command = "tail -F /var/log/app.log",
        });

        Assert.Equal("deploy@server.example: tail -F /var/log/app.log", source.DisplayName);
    }

    [Fact]
    public async Task NoCredentials_RaisesError()
    {
        using var source = new SshTailSource(new SshTailOptions
        {
            Host = "server.example",
            Username = "deploy",
            Command = "tail -F /var/log/app.log",
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
        Assert.Contains("password or a private key", error!.Message);
    }
}
