using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace LogViewer.Core.EventLogging;

/// <summary>Shared filter-matching semantics for a set of <see cref="EventLogFilterRule"/>s, used by both live
/// tailing (<see cref="WindowsEventLogSource"/>) and full-channel search (<see cref="EventLogSearchService"/>).
/// No enabled filter means everything passes; otherwise an event must match at least one enabled filter (OR).
/// Not thread-safe — callers touching the same instance from multiple threads (e.g. a live watcher callback
/// racing a filter-set update) must guard access with their own lock.</summary>
[SupportedOSPlatform("windows")]
internal sealed class EventLogFilterEvaluator
{
    private readonly Dictionary<Guid, Regex> _compiledRegexCache = new();
    private IReadOnlyList<EventLogFilterRule> _enabledFilters = [];

    /// <summary>Replaces the filter set, precomputing the enabled subset and dropping cached regexes for
    /// filters that are no longer active — mirrors <see cref="Highlighting.HighlightEngine.SetRules"/>.</summary>
    public void SetFilters(IReadOnlyList<EventLogFilterRule> filters)
    {
        _enabledFilters = filters.Where(f => f.IsEnabled).ToList();

        var activeIds = _enabledFilters.Select(f => f.Id).ToHashSet();
        foreach (var staleId in _compiledRegexCache.Keys.Except(activeIds).ToList())
        {
            _compiledRegexCache.Remove(staleId);
        }
    }

    public bool PassesFilters(EventRecord record, string formattedMessage)
    {
        if (_enabledFilters.Count == 0)
        {
            return true;
        }

        foreach (var filter in _enabledFilters)
        {
            if (!string.IsNullOrEmpty(filter.ProviderName) &&
                !string.Equals(record.ProviderName, filter.ProviderName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = filter.Field switch
            {
                EventLogFilterField.ProviderName => record.ProviderName ?? string.Empty,
                EventLogFilterField.Level => EventRecordFormatter.SafeGet(() => record.LevelDisplayName) ?? string.Empty,
                _ => formattedMessage,
            };

            var regex = GetCompiledRegex(filter);
            if (regex is null)
            {
                continue;
            }

            try
            {
                if (regex.IsMatch(value))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pathological pattern on this input — skip this filter rather than failing the whole evaluation.
            }
        }

        return false;
    }

    private Regex? GetCompiledRegex(EventLogFilterRule filter)
    {
        if (_compiledRegexCache.TryGetValue(filter.Id, out var cached))
        {
            return cached;
        }

        Regex regex;
        try
        {
            regex = new Regex(filter.RegexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            // Invalid regex — nothing to cache; every call will retry the (cheap) parse until fixed.
            return null;
        }

        _compiledRegexCache[filter.Id] = regex;
        return regex;
    }
}
