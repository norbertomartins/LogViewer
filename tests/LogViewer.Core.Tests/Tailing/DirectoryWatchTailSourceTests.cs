using System.Collections.Concurrent;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class DirectoryWatchTailSourceTests
{
    private static readonly TailSourceOptions FastPollOptions = new() { PollInterval = TimeSpan.FromMilliseconds(30) };

    [Fact]
    public void Start_TailsTheOnlyMatchingFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pathA = Path.Combine(directory, "a.log");
            File.WriteAllText(pathA, "hello from a\n");

            var received = new ConcurrentQueue<string>();
            using var source = new DirectoryWatchTailSource(directory, "*.log", autoSwitchEnabled: true, FastPollOptions);
            source.LinesRead += (_, e) => Enqueue(received, e.Lines);
            source.Start();

            Assert.True(WaitUntil(() => received.Contains("hello from a")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AutoSwitch_WhenNewerFileAppears_SwitchesAndRaisesReset()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pathA = Path.Combine(directory, "a.log");
            File.WriteAllText(pathA, "content a\n");

            var received = new ConcurrentQueue<string>();
            var resets = new ConcurrentQueue<TailResetReason>();
            using var source = new DirectoryWatchTailSource(directory, "*.log", autoSwitchEnabled: true, FastPollOptions);
            source.LinesRead += (_, e) => Enqueue(received, e.Lines);
            source.SourceReset += (_, e) => resets.Enqueue(e.Reason);
            source.Start();

            Assert.True(WaitUntil(() => received.Contains("content a")));

            var pathB = Path.Combine(directory, "b.log");
            File.WriteAllText(pathB, "content b\n");
            File.SetLastWriteTimeUtc(pathB, DateTime.UtcNow.AddSeconds(5));

            Assert.True(WaitUntil(() => received.Contains("content b")), "Expected auto-switch to the newer file's content.");
            Assert.True(WaitUntil(() => resets.Contains(TailResetReason.Rotated)), "Expected a reset when switching files.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AutoSwitch_ResetIsRaisedBeforeTheNewFilesContent()
    {
        // Consumers clear their display on SourceReset, so if the new file's LinesRead arrived first,
        // the reset that follows would wipe out content that was already delivered. Regression for a
        // real ordering bug caught via manual UI testing.
        var directory = CreateTempDirectory();
        try
        {
            var pathA = Path.Combine(directory, "a.log");
            File.WriteAllText(pathA, "content a\n");

            var events = new ConcurrentQueue<string>();
            using var source = new DirectoryWatchTailSource(directory, "*.log", autoSwitchEnabled: true, FastPollOptions);
            source.LinesRead += (_, e) =>
            {
                foreach (var line in e.Lines)
                {
                    events.Enqueue("lines:" + line.Text);
                }
            };
            source.SourceReset += (_, _) => events.Enqueue("reset");
            source.Start();

            Assert.True(WaitUntil(() => events.Any(e => e == "lines:content a")));

            var pathB = Path.Combine(directory, "b.log");
            File.WriteAllText(pathB, "content b\n");
            File.SetLastWriteTimeUtc(pathB, DateTime.UtcNow.AddSeconds(5));

            Assert.True(WaitUntil(() => events.Any(e => e == "lines:content b")));

            var ordered = events.ToArray();
            var resetIndex = Array.IndexOf(ordered, "reset");
            var contentBIndex = Array.IndexOf(ordered, "lines:content b");
            Assert.True(resetIndex >= 0 && contentBIndex >= 0 && resetIndex < contentBIndex,
                $"Expected 'reset' before 'lines:content b', got: {string.Join(", ", ordered)}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AutoSwitchDisabled_StaysOnInitialFile()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pathA = Path.Combine(directory, "a.log");
            File.WriteAllText(pathA, "content a\n");

            var received = new ConcurrentQueue<string>();
            using var source = new DirectoryWatchTailSource(directory, "*.log", autoSwitchEnabled: false, FastPollOptions);
            source.LinesRead += (_, e) => Enqueue(received, e.Lines);
            source.Start();

            Assert.True(WaitUntil(() => received.Contains("content a")));

            var pathB = Path.Combine(directory, "b.log");
            File.WriteAllText(pathB, "content b\n");
            File.SetLastWriteTimeUtc(pathB, DateTime.UtcNow.AddSeconds(5));

            Thread.Sleep(300);
            Assert.DoesNotContain("content b", received);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "LogViewerDirTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Enqueue(ConcurrentQueue<string> queue, IReadOnlyList<TailLine> lines)
    {
        foreach (var line in lines)
        {
            queue.Enqueue(line.Text);
        }
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
