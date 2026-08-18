using BenchmarkDotNet.Attributes;
using LogViewer.App.Models;
using LogViewer.Core.Structured;

namespace LogViewer.Benchmarks;

/// <summary>
/// Validates the caching added to TailDocumentViewModel.StructuredLines
/// (LogViewer.App/ViewModels/TailDocumentViewModel.cs): recomputing the Where/Select/ToList projection
/// on every access (old behavior — read on every similar-block lookup, sometimes more than once per
/// lookup) vs computing it once and reusing it until the underlying lines change (current behavior,
/// invalidated via DisplayLineCollection's Reset notification).
/// </summary>
[MemoryDiagnoser]
public class StructuredLinesCachingBenchmarks
{
    private List<LogLineViewModel> _lines = [];
    private List<(long LineNumber, StructuredLogEvent Event)>? _cache;

    [Params(5_000, 50_000)]
    public int LineCount { get; set; }

    /// <summary>How many times a single similar-block lookup reads StructuredLines while building an
    /// anchor block — modeling BuildAnchorBlock touching it once to materialize the pool and once more
    /// while resolving the anchor index.</summary>
    [Params(1, 3)]
    public int ReadsPerLookup { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lines = new List<LogLineViewModel>(LineCount);
        for (var i = 1; i <= LineCount; i++)
        {
            var structured = i % 3 == 0
                ? new StructuredLogEvent(DateTimeOffset.UtcNow, "Information", null, $"event {i}", null, new Dictionary<string, string>())
                : null;
            _lines.Add(new LogLineViewModel(i, $"line {i}", structured, match: null, isBookmarked: false));
        }
    }

    [Benchmark(Baseline = true)]
    public int Old_RecomputeEveryAccess()
    {
        var total = 0;
        for (var i = 0; i < ReadsPerLookup; i++)
        {
            var projected = _lines.Where(l => l.Structured is not null).Select(l => (l.LineNumber, l.Structured!)).ToList();
            total += projected.Count;
        }

        return total;
    }

    [Benchmark]
    public int Current_CachedAccess()
    {
        _cache = null; // One invalidation per document update, then reused across ReadsPerLookup accesses.
        var total = 0;
        for (var i = 0; i < ReadsPerLookup; i++)
        {
            _cache ??= _lines.Where(l => l.Structured is not null).Select(l => (l.LineNumber, l.Structured!)).ToList();
            total += _cache.Count;
        }

        return total;
    }
}
