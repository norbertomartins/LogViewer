using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using LogViewer.Core.EventLogging;

namespace LogViewer.Benchmarks;

/// <summary>
/// Validates the fix in EventLogFilterEvaluator (LogViewer.Core/EventLogging/EventLogFilterEvaluator.cs):
/// a precomputed enabled-filter list plus a per-rule compiled <see cref="Regex"/> cache (current), vs
/// recomputing the enabled list and calling the static, uncompiled <see cref="Regex.IsMatch(string, string)"/>
/// overload per event (old behavior — the static regex cache only holds 15 entries process-wide, so it
/// thrashes once a channel scan uses more than a handful of distinct patterns).
/// </summary>
[SupportedOSPlatform("windows")]
[MemoryDiagnoser]
public class EventLogFilterEvaluatorBenchmarks
{
    private const string Message =
        "Service 'Contoso.Worker' failed to start: connection to database timed out after 30000ms (attempt 3 of 5, correlation=8f2a91cd-4b3e-4a2f-9c1a-1234567890ab)";

    private List<EventLogFilterRule> _filters = [];
    private EventLogFilterEvaluator _evaluator = new();

    [Params(1, 5, 20)]
    public int FilterCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        // Every rule targets the default field (Message) with no provider filter, so PassesFilters never
        // dereferences the EventRecord parameter — safe to pass null in the benchmark below.
        _filters = Enumerable.Range(0, FilterCount)
            .Select(i => EventLogFilterRule.CreateDefault($"rule-{i}", $@"attempt \d+ of \d+.*correlation=[0-9a-f-]{{36}}.*{i % 7}"))
            .ToList();

        _evaluator = new EventLogFilterEvaluator();
        _evaluator.SetFilters(_filters);
    }

    /// <summary>Reproduces the pre-fix EventLogFilterEvaluator.PassesFilters body verbatim.</summary>
    [Benchmark(Baseline = true)]
    public bool Old_RecomputeEnabledAndUncachedRegex()
    {
        var enabled = _filters.Where(f => f.IsEnabled).ToList();
        foreach (var filter in enabled)
        {
            if (Regex.IsMatch(Message, filter.RegexPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    [Benchmark]
    public bool Current_CachedCompiledRegex() => _evaluator.PassesFilters(null!, Message);
}
