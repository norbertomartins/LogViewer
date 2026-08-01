using System.Text.RegularExpressions;
using LogViewer.Core.Theming;

namespace LogViewer.Core.Highlighting;

/// <summary>
/// Evaluates a line against a set of highlight rules. Keyword rules use plain substring search;
/// regex rules use cached compiled <see cref="Regex"/> instances. Designed to run on the same
/// background batch step as tailing, not on the UI thread.
/// </summary>
public sealed class HighlightEngine
{
    private readonly Dictionary<Guid, Regex> _compiledRegexCache = new();
    private IReadOnlyList<HighlightRule> _rulesByPriorityDescending = [];
    private ThemeBaseMode _themeMode = ThemeBaseMode.Light;

    /// <summary>Which color pair (<see cref="HighlightRule.ResolveColors"/>) matches use from now on.
    /// Lines already displayed keep their original colors, same as an <see cref="SetRules"/> update.</summary>
    public void SetThemeMode(ThemeBaseMode mode) => _themeMode = mode;

    public void SetRules(IEnumerable<HighlightRule> rules)
    {
        _rulesByPriorityDescending = rules
            .Where(r => r.IsEnabled)
            .OrderByDescending(r => r.Priority)
            .ToList();

        var activeIds = _rulesByPriorityDescending.Select(r => r.Id).ToHashSet();
        foreach (var staleId in _compiledRegexCache.Keys.Except(activeIds).ToList())
        {
            _compiledRegexCache.Remove(staleId);
        }
    }

    public HighlightMatch? Evaluate(string line)
    {
        foreach (var rule in _rulesByPriorityDescending)
        {
            if (IsMatch(rule, line))
            {
                var (foreground, background) = rule.ResolveColors(_themeMode);
                return new HighlightMatch(rule.Id, foreground, background);
            }
        }

        return null;
    }

    private bool IsMatch(HighlightRule rule, string line)
    {
        if (string.IsNullOrEmpty(rule.Pattern))
        {
            return false;
        }

        if (!rule.IsRegex)
        {
            var comparison = rule.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return line.Contains(rule.Pattern, comparison);
        }

        if (!_compiledRegexCache.TryGetValue(rule.Id, out var regex))
        {
            var options = RegexOptions.Compiled | (rule.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(rule.Pattern, options, TimeSpan.FromSeconds(1));
            _compiledRegexCache[rule.Id] = regex;
        }

        try
        {
            return regex.IsMatch(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
