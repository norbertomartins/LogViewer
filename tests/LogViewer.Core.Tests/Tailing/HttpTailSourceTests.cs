using System.Net;
using System.Net.Http;
using System.Text;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class HttpTailSourceTests
{
    private static readonly Uri Url = new("https://logs.example/tail");

    private sealed class QueueHandler(Func<int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(Interlocked.Increment(ref _count) - 1));
    }

    private static HttpResponseMessage Text(string body, string mediaType = "text/plain")
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task<List<string>> Collect(HttpTailSource source, Func<List<string>, bool> until, TimeSpan timeout)
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
    public async Task Poll_EmitsOnlyNewLinesEachRequest()
    {
        var bodies = new[] { "a\nb\n", "a\nb\n", "a\nb\nc\n", "a\nb\nc\nd\n" };
        var handler = new QueueHandler(i => Text(bodies[Math.Min(i, bodies.Length - 1)]));
        using var source = new HttpTailSource(Url, new HttpTailOptions
        {
            Mode = HttpTailMode.Poll,
            PollInterval = TimeSpan.FromMilliseconds(40),
        }, handler);

        var lines = await Collect(source, l => l.Contains("d"), TimeSpan.FromSeconds(5));

        Assert.Equal(["a", "b", "c", "d"], lines);
    }

    [Fact]
    public async Task Poll_ShrinkingBody_RaisesReset()
    {
        var bodies = new[] { "1\n2\n3\n", "1\n2\n3\n", "9\n" };
        var handler = new QueueHandler(i => Text(bodies[Math.Min(i, bodies.Length - 1)]));
        using var source = new HttpTailSource(Url, new HttpTailOptions
        {
            Mode = HttpTailMode.Poll,
            PollInterval = TimeSpan.FromMilliseconds(40),
        }, handler);

        var resets = 0;
        source.SourceReset += (_, _) => Interlocked.Increment(ref resets);

        var lines = await Collect(source, l => l.Contains("9"), TimeSpan.FromSeconds(5));

        Assert.True(resets >= 1);
        Assert.Equal(["1", "2", "3", "9"], lines);
    }

    [Fact]
    public async Task Stream_Sse_StripsDataPrefixAndSkipsControlFrames()
    {
        var sse = "event: message\ndata: first line\n\n: keep-alive\ndata: second line\n\n";
        var handler = new QueueHandler(_ => Text(sse, "text/event-stream"));
        using var source = new HttpTailSource(Url, new HttpTailOptions { Mode = HttpTailMode.Stream }, handler);

        var lines = await Collect(source, l => l.Count >= 2, TimeSpan.FromSeconds(5));

        Assert.Equal(["first line", "second line"], lines.Take(2));
    }

    [Fact]
    public async Task Auto_DetectsStreamFromContentType()
    {
        var handler = new QueueHandler(_ => Text("data: x\n\ndata: y\n\n", "text/event-stream"));
        using var source = new HttpTailSource(Url, new HttpTailOptions { Mode = HttpTailMode.Auto }, handler);

        var lines = await Collect(source, l => l.Count >= 2, TimeSpan.FromSeconds(5));

        Assert.Equal(["x", "y"], lines.Take(2));
    }
}
