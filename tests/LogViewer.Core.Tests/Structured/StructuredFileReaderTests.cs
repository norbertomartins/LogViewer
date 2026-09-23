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
    public async Task ReadAsync_PlainTextLog_YieldsEveryLineWithAGuessedLevelAndTimestamp()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(path,
            [
                "2026-02-15 09:00:03.000 [INFO] Payment pay_514002 captured",
                "",
                "2026-02-15 09:00:09.000 [ERROR] Gateway timeout for pay_632084",
                "   at Payments.Gateway.Send()",
            ]);

            var events = new List<(long, StructuredLogEvent)>();
            await foreach (var e in StructuredFileReader.ReadAsync(path, CancellationToken.None))
            {
                events.Add(e);
            }

            Assert.Equal([1L, 3L, 4L], events.Select(e => e.Item1));
            Assert.Equal("Information", events[0].Item2.Level);
            Assert.Equal("Error", events[1].Item2.Level);
            Assert.Null(events[2].Item2.Level);
            Assert.Equal(new DateTime(2026, 2, 15, 9, 0, 9), events[1].Item2.Timestamp?.DateTime);
            Assert.Equal("2026-02-15 09:00:03.000 [INFO] Payment pay_514002 captured", events[0].Item2.RenderedMessage);
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
