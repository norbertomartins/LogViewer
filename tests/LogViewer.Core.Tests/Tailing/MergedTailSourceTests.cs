using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class MergedTailSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"merge-{Guid.NewGuid():N}");

    public MergedTailSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, params string[] lines)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void InitialContent_IsMergedInTimestampOrder_WithLabels()
    {
        var a = Write("svc-a.log",
            "2026-01-02 10:00:01 INFO a-first",
            "2026-01-02 10:00:03 INFO a-third");
        var b = Write("svc-b.log",
            "2026-01-02 10:00:02 INFO b-second",
            "2026-01-02 10:00:04 INFO b-fourth");

        var clock = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        using var merged = new MergedTailSource([a, b], reorderWindow: TimeSpan.FromSeconds(2), clock: () => clock);

        var received = new List<string>();
        merged.LinesRead += (_, e) => received.AddRange(e.Lines.Select(l => l.Text));

        merged.Start();
        merged.FlushDueAt(clock.AddMinutes(1));

        Assert.Equal(
        [
            "svc-a│ 2026-01-02 10:00:01 INFO a-first",
            "svc-b│ 2026-01-02 10:00:02 INFO b-second",
            "svc-a│ 2026-01-02 10:00:03 INFO a-third",
            "svc-b│ 2026-01-02 10:00:04 INFO b-fourth",
        ], received);
    }

    [Fact]
    public void EmittedLineNumbers_AreSequential()
    {
        var a = Write("a.log", "2026-01-02 10:00:01 x");
        var b = Write("b.log", "2026-01-02 10:00:02 y");

        var clock = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        using var merged = new MergedTailSource([a, b], clock: () => clock);

        var numbers = new List<long>();
        merged.LinesRead += (_, e) => numbers.AddRange(e.Lines.Select(l => l.LineNumber));

        merged.Start();
        merged.FlushDueAt(clock.AddMinutes(1));

        Assert.Equal([1, 2], numbers);
    }

    [Fact]
    public void LinesWithinReorderWindow_AreNotFlushedYet()
    {
        var a = Write("a.log", "2026-01-02 10:00:01 x");
        var b = Write("b.log", "2026-01-02 10:00:02 y");

        var clock = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        using var merged = new MergedTailSource([a, b], reorderWindow: TimeSpan.FromSeconds(5), clock: () => clock);

        var count = 0;
        merged.LinesRead += (_, e) => count += e.Lines.Count;

        merged.Start();
        merged.FlushDueAt(clock.AddSeconds(1)); // still inside the 5s window

        Assert.Equal(0, count);
    }

    [Fact]
    public void ContinuationLineWithoutTimestamp_InheritsPrecedingTimestamp()
    {
        var a = Write("a.log",
            "2026-01-02 10:00:05 ERROR boom",
            "   at Foo.Bar()",
            "   at Baz.Qux()");
        var b = Write("b.log", "2026-01-02 10:00:04 INFO earlier");

        var clock = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero);
        using var merged = new MergedTailSource([a, b], clock: () => clock);

        var received = new List<string>();
        merged.LinesRead += (_, e) => received.AddRange(e.Lines.Select(l => l.Text));

        merged.Start();
        merged.FlushDueAt(clock.AddMinutes(1));

        Assert.Equal(
        [
            "b│ 2026-01-02 10:00:04 INFO earlier",
            "a│ 2026-01-02 10:00:05 ERROR boom",
            "a│    at Foo.Bar()",
            "a│    at Baz.Qux()",
        ], received);
    }

    [Fact]
    public void FewerThanTwoPaths_Throws()
    {
        Assert.Throws<ArgumentException>(() => new MergedTailSource([Write("only.log", "x")]));
    }

    [Fact]
    public void AssignLabels_DisambiguatesCollidingBaseNames()
    {
        var labels = MergedTailSource.AssignLabels([@"c:\one\app.log", @"c:\two\app.log", @"c:\three\web.log"])
            .Select(x => x.Label)
            .ToList();

        Assert.Equal(["app", "app#2", "web"], labels);
    }
}
