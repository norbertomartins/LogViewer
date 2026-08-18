using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;

namespace LogViewer.Benchmarks;

/// <summary>
/// Documents why LogViewer.Core/Search/FileFullTextSearchService.cs and
/// LogViewer.Core/EventLogging/EventLogSearchService.cs deliberately do NOT use
/// <see cref="RegexOptions.Compiled"/>: both build the search regex once per search, then call
/// <see cref="Regex.IsMatch(string)"/> once per streamed line/event — a one-shot usage where Compiled's
/// ~4.3ms one-time JIT cost isn't recouped by faster per-call matching until roughly tens of thousands
/// of scanned lines. <see cref="Current_InterpretedNewRegexPerSearch"/> is what ships;
/// <see cref="Rejected_CompiledNewRegexPerSearch"/> is the alternative that was tried and reverted after
/// this benchmark showed it regressed typical (sub-10k-line) searches. Contrast with
/// <see cref="EventLogFilterEvaluatorBenchmarks"/>, where Compiled genuinely wins because the same regex
/// instance is cached and reused across many events.
/// </summary>
[MemoryDiagnoser]
public class FullTextSearchRegexBenchmarks
{
    private const string Pattern = @"ERROR.*correlation=([0-9a-f-]{36}).*attempt (\d+)";

    private string[] _lines = [];

    [Params(1_000, 50_000)]
    public int LineCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _lines = Enumerable.Range(0, LineCount)
            .Select(i => i % 17 == 0
                ? $"2026-08-18 12:00:{i % 60:00} ERROR Worker failed correlation={Guid.NewGuid()} attempt {i % 5} of 5"
                : $"2026-08-18 12:00:{i % 60:00} INFO  Worker heartbeat #{i}")
            .ToArray();
    }

    [Benchmark(Baseline = true)]
    public int Current_InterpretedNewRegexPerSearch()
    {
        var regex = new Regex(Pattern, RegexOptions.IgnoreCase);
        return CountMatches(regex);
    }

    [Benchmark]
    public int Rejected_CompiledNewRegexPerSearch()
    {
        var regex = new Regex(Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        return CountMatches(regex);
    }

    private int CountMatches(Regex regex)
    {
        var count = 0;
        foreach (var line in _lines)
        {
            if (regex.IsMatch(line))
            {
                count++;
            }
        }

        return count;
    }
}
