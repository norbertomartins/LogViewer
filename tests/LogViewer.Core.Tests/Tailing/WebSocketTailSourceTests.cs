using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class WebSocketFramesTests
{
    [Fact]
    public void SplitMessage_SingleUnterminatedLine_YieldsThatLine() =>
        Assert.Equal(["2026-01-02 INFO hi"], WebSocketFrames.SplitMessage("2026-01-02 INFO hi"));

    [Fact]
    public void SplitMessage_MultipleLines_WithTrailingNewline() =>
        Assert.Equal(["a", "b", "c"], WebSocketFrames.SplitMessage("a\r\nb\nc\n"));

    [Fact]
    public void SplitMessage_Empty_YieldsNothing() =>
        Assert.Empty(WebSocketFrames.SplitMessage(string.Empty));
}

public sealed class WebSocketTailSourceTests
{
    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task ReceivesTextFrames_AsLines_OverLoopbackWebSocket()
    {
        var port = FreeTcpPort();
        var prefix = $"http://localhost:{port}/";
        using var httpListener = new HttpListener();
        httpListener.Prefixes.Add(prefix);

        try
        {
            httpListener.Start();
        }
        catch (HttpListenerException)
        {
            return; // no URL-ACL permission in this environment — nothing to assert
        }

        using var serverDone = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = Task.Run(async () =>
        {
            var context = await httpListener.GetContextAsync();
            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            var socket = wsContext.WebSocket;

            foreach (var line in new[] { "line one", "line two\nline three", "line four" })
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(line), WebSocketMessageType.Text, endOfMessage: true, serverDone.Token);
                await Task.Delay(30, serverDone.Token);
            }

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", serverDone.Token);
        }, serverDone.Token);

        var uri = new Uri($"ws://localhost:{port}/");
        using var source = new WebSocketTailSource(uri, new WebSocketTailOptions { FlushInterval = TimeSpan.FromMilliseconds(50) });

        var received = new List<string>();
        source.LinesRead += (_, e) =>
        {
            lock (received)
            {
                received.AddRange(e.Lines.Select(l => l.Text));
            }
        };

        source.Start();

        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            lock (received)
            {
                if (received.Count >= 4)
                {
                    break;
                }
            }

            await Task.Delay(25);
        }

        source.Stop();
        httpListener.Stop();

        Assert.Equal(["line one", "line two", "line three", "line four"], received.Take(4));
    }
}
