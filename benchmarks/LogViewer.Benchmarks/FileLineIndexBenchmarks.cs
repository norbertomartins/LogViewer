using System.IO;
using System.Text;
using BenchmarkDotNet.Attributes;
using LogViewer.Core.Indexing;
using LogViewer.Core.Tailing;

namespace LogViewer.Benchmarks;

/// <summary>
/// Load test for the whole-file browser's <see cref="FileLineIndex"/> on multi-GB logs: the first full scan, a
/// reopen served from the persisted index cache, reading a page from the middle, a go-to-time binary search and a
/// full-file match count (what the browser's search does). The generated files (~100 bytes/line, ascending
/// timestamps, 1 in 50 lines an ERROR) are kept in <c>%TEMP%\LogViewer.Benchmarks</c> and reused across runs,
/// because BenchmarkDotNet runs each benchmark in its own process and rewriting 2 GB each time would dominate.
/// Run with <c>--filter *FileLineIndex*</c>.
/// </summary>
[MemoryDiagnoser]
public class FileLineIndexBenchmarks
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private string _path = string.Empty;
    private string _cacheDirectory = string.Empty;
    private FileLineIndex _index = null!;
    private DateTimeOffset _middleTime;

    [Params(256, 2048)]
    public int FileSizeMb { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LogViewer.Benchmarks");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, $"index-{FileSizeMb}mb.log");
        _cacheDirectory = Path.Combine(directory, $"index-cache-{FileSizeMb}mb");
        var lineCount = EnsureFile(_path, FileSizeMb * 1024L * 1024L);
        _middleTime = Start.AddMilliseconds(lineCount / 2 * 10);

        // Prebuilt index for the read benchmarks, and a persisted cache entry for the reopen benchmark.
        Directory.CreateDirectory(_cacheDirectory);
        FileLineIndex.PersistDirectory = _cacheDirectory;
        _index = new FileLineIndex(_path);
        _index.UpdateAsync().GetAwaiter().GetResult();
        FileLineIndex.PersistDirectory = null;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        FileLineIndex.PersistDirectory = null;
        try
        {
            Directory.Delete(_cacheDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort.
        }
    }

    [Benchmark]
    public long FullScan()
    {
        FileLineIndex.PersistDirectory = null;
        var index = new FileLineIndex(_path);
        index.UpdateAsync().GetAwaiter().GetResult();
        return index.LineCount;
    }

    [Benchmark]
    public long ReopenFromPersistedCache()
    {
        FileLineIndex.PersistDirectory = _cacheDirectory;
        try
        {
            var index = new FileLineIndex(_path);
            index.UpdateAsync().GetAwaiter().GetResult();
            return index.LoadedFromCache ? index.LineCount : throw new InvalidOperationException("The cache was not used.");
        }
        finally
        {
            FileLineIndex.PersistDirectory = null;
        }
    }

    [Benchmark]
    public int ReadPageFromTheMiddle() => _index.ReadLines(_index.LineCount / 2, 200).Count;

    [Benchmark]
    public long? GoToTimeInTheMiddle() => _index.FindFirstLineAtOrAfter(_middleTime, MergedTimestampExtractor.TryExtract);

    [Benchmark]
    public long CountErrorLines() => _index.CountMatches(line => line.Contains("ERROR", StringComparison.Ordinal));

    /// <summary>Writes the fixture unless a file of at least the target size already exists; returns its line count.</summary>
    private static long EnsureFile(string path, long targetBytes)
    {
        const int LineBytes = 100;
        if (File.Exists(path) && new FileInfo(path).Length >= targetBytes)
        {
            return new FileInfo(path).Length / LineBytes;
        }

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false), bufferSize: 1 << 20);
        var line = new StringBuilder(LineBytes);
        long written = 0;
        long number = 0;
        while (written < targetBytes)
        {
            line.Clear();
            var time = Start.AddMilliseconds(number * 10);
            line.Append(time.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
            line.Append(number % 50 == 0 ? " [ERROR] Payment gateway timeout for order " : " [INFO] Handled request for order ");
            line.Append(number.ToString("D10", System.Globalization.CultureInfo.InvariantCulture));
            line.Append(' ', Math.Max(0, LineBytes - 1 - line.Length));
            writer.Write(line);
            writer.Write('\n');
            written += line.Length + 1;
            number++;
        }

        return number;
    }
}
