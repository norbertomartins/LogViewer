using System.Text.RegularExpressions;
using LogViewer.Core.Structured;
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
    private IReadOnlyList<HighlightRule> _rulesInMatchOrder = [];
    private ThemeBaseMode _themeMode = ThemeBaseMode.Light;

    /// <summary>Which color pair (<see cref="HighlightRule.ResolveColors"/>) matches use from now on.
    /// Lines already displayed keep their original colors, same as an <see cref="SetRules"/> update.</summary>
    public void SetThemeMode(ThemeBaseMode mode) => _themeMode = mode;

    /// <summary>Sets the rules to match against, in the order they should be tried — the first rule to match a
    /// line wins. Callers with presets should flatten via <see cref="HighlightPreset.FlattenForMatching"/> first.</summary>
    public void SetRules(IEnumerable<HighlightRule> rules)
    {
        _rulesInMatchOrder = rules.Where(r => r.IsEnabled).ToList();

        var activeIds = _rulesInMatchOrder.Select(r => r.Id).ToHashSet();
        foreach (var staleId in _compiledRegexCache.Keys.Except(activeIds).ToList())
        {
            _compiledRegexCache.Remove(staleId);
        }
    }

    /// <summary>Evaluates a line against the current rules. <paramref name="structured"/> is the line's parsed
    /// Serilog event when the document is in structured view, else null — required for rules with a
    /// <see cref="HighlightRule.TargetProperty"/>, which never match when it's null.</summary>
    public HighlightMatch? Evaluate(string line, StructuredLogEvent? structured = null)
    {
        foreach (var rule in _rulesInMatchOrder)
        {
            if (IsMatch(rule, line, structured))
            {
                var (foreground, background) = rule.ResolveColors(_themeMode);
                return new HighlightMatch(rule.Id, foreground, background);
            }
        }

        return null;
    }

    private bool IsMatch(HighlightRule rule, string line, StructuredLogEvent? structured)
    {
        if (string.IsNullOrEmpty(rule.Pattern))
        {
            return false;
        }

        var candidate = string.IsNullOrEmpty(rule.TargetProperty)
            ? line
            : StructuredFieldResolver.Resolve(structured, rule.TargetProperty);

        if (candidate is null)
        {
            return false;
        }

        if (!rule.IsRegex)
        {
            var comparison = rule.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return candidate.Contains(rule.Pattern, comparison);
        }

        if (!_compiledRegexCache.TryGetValue(rule.Id, out var regex))
        {
            var options = RegexOptions.Compiled | (rule.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(rule.Pattern, options, TimeSpan.FromSeconds(1));
            _compiledRegexCache[rule.Id] = regex;
        }

        try
        {
            return regex.IsMatch(candidate);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
