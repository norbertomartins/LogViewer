using BenchmarkDotNet.Attributes;
using LogViewer.App.Controls;
using LogViewer.App.Models;

namespace LogViewer.Benchmarks;

/// <summary>
/// Validates the O(1) line-number index added to LogViewer.App/Controls/DisplayLineCollection.cs's
/// FindByLineNumber: the old implementation was a linear <see cref="List{T}.Find"/> scan, called on
/// every search-result jump and bookmark/highlight navigation. The baseline reproduces that scan
/// directly over the same backing data.
/// </summary>
[MemoryDiagnoser]
public class DisplayLineCollectionBenchmarks
{
    private DisplayLineCollection _collection = null!;
    private List<LogLineViewModel> _baselineItems = [];
    private long _targetLineNumber;

    [Params(5_000, 50_000)]
    public int Capacity { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _collection = new DisplayLineCollection(Capacity);
        var lines = new List<LogLineViewModel>(Capacity);
        for (var i = 1; i <= Capacity; i++)
        {
            lines.Add(new LogLineViewModel(i, $"line {i}", structured: null, match: null, isBookmarked: false));
        }

        _collection.AppendRange(lines);
        _baselineItems = lines;

        // Worst case for a linear scan: the line the user jumps to sits near the end of the buffer.
        _targetLineNumber = Capacity - 5;
    }

    [Benchmark(Baseline = true)]
    public LogLineViewModel? Old_LinearScan() => _baselineItems.Find(l => l.LineNumber == _targetLineNumber);

    [Benchmark]
    public LogLineViewModel? Current_IndexedLookup() => _collection.FindByLineNumber(_targetLineNumber);
}
