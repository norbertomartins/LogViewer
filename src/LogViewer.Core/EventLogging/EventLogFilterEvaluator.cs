using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace LogViewer.Core.EventLogging;

/// <summary>Shared filter-matching semantics for a set of <see cref="EventLogFilterRule"/>s, used by both live
/// tailing (<see cref="WindowsEventLogSource"/>) and full-channel search (<see cref="EventLogSearchService"/>).
/// No enabled filter means everything passes; otherwise an event must match at least one enabled filter (OR).</summary>
[SupportedOSPlatform("windows")]
internal static class EventLogFilterEvaluator
{
    public static bool PassesFilters(EventRecord record, string formattedMessage, IReadOnlyList<EventLogFilterRule> filters)
    {
        var enabled = filters.Where(f => f.IsEnabled).ToList();
        if (enabled.Count == 0)
        {
            return true;
        }

        foreach (var filter in enabled)
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

            try
            {
                if (Regex.IsMatch(value, filter.RegexPattern, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Invalid regex — skip this filter rather than failing the whole evaluation.
            }
        }

        return false;
    }
}
