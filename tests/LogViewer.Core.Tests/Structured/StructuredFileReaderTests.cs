using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class StructuredFileReaderTests
{
    [Fact]
    public async Task ReadAsync_AutoDetectsLogfmt()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path,
            [
                "ts=2026-01-02T03:04:05Z level=info msg=start svc=api",
                "ts=2026-01-02T03:04:06Z level=error msg=\"db timeout\" svc=api",
            ]);

            var events = new List<(long, StructuredLogEvent)>();
            await foreach (var e in StructuredFileReader.ReadAsync(path, CancellationToken.None))
            {
                events.Add(e);
            }

            Assert.Equal(2, events.Count);
            Assert.Equal("start", events[0].Item2.RenderedMessage);
            Assert.Equal("Error", events[1].Item2.Level);
            Assert.Equal("api", events[1].Item2.Properties["svc"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_ExplicitParser_IsUsed()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path, ["<11>1 2026-01-02T03:04:05Z h app - - - disk failure"]);

            var events = new List<(long, StructuredLogEvent)>();
            await foreach (var e in StructuredFileReader.ReadAsync(path, new SyslogLogLineParser(), CancellationToken.None))
            {
                events.Add(e);
            }

            Assert.Single(events);
            Assert.Equal("disk failure", events[0].Item2.RenderedMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
