using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LogViewer.Core.Structured;
using LogViewer.Core.Theming;

namespace LogViewer.Core.Highlighting;

/// <summary>
/// Evaluates a line against a set of highlight rules. Keyword rules use plain substring search;
/// regex rules use cached compiled <see cref="Regex"/> instances. Designed to run on the same
/// background batch step as tailing, not on the UI thread.
/// <para><see cref="Evaluate"/> is safe to call from any thread, concurrently with <see cref="SetRules"/>/
/// <see cref="SetThemeMode"/> on another: rules are swapped as an immutable snapshot and the regex cache is
/// concurrent. A call racing a rules change may use either the old or the new rules — callers that care (the
/// document's background pre-evaluation) detect that with their own version stamp and re-evaluate.</para>
/// </summary>
public sealed class HighlightEngine
{
    private readonly ConcurrentDictionary<Guid, Regex> _compiledRegexCache = new();
    private volatile IReadOnlyList<HighlightRule> _rulesInMatchOrder = [];
    private volatile ThemeBaseMode _themeMode = ThemeBaseMode.Light;

    /// <summary>Which color pair (<see cref="HighlightRule.ResolveColors"/>) matches use from now on.
    /// Lines already displayed keep their original colors, same as an <see cref="SetRules"/> update.</summary>
    public void SetThemeMode(ThemeBaseMode mode) => _themeMode = mode;

    /// <summary>Sets the rules to match against, in the order they should be tried — the first rule to match a
    /// line wins. Callers with presets should flatten via <see cref="HighlightPreset.FlattenForMatching"/> first.</summary>
    public void SetRules(IEnumerable<HighlightRule> rules)
    {
        var active = rules.Where(r => r.IsEnabled).ToList();

        // Drop cached regexes of rules that are gone or whose pattern/case changed (a rule keeps its id when edited).
        var activeById = active.ToDictionary(r => r.Id);
        foreach (var (id, regex) in _compiledRegexCache)
        {
            if (!activeById.TryGetValue(id, out var rule) || !IsCompiledFor(regex, rule))
            {
                _compiledRegexCache.TryRemove(id, out _);
            }
        }

        _rulesInMatchOrder = active;
    }

    /// <summary>Evaluates a line against the current rules. <paramref name="structured"/> is the line's parsed
    /// Serilog event when the document is in structured view, else null — required for rules with a
    /// <see cref="HighlightRule.TargetProperty"/>, which never match when it's null.</summary>
    public HighlightMatch? Evaluate(string line, StructuredLogEvent? structured = null)
    {
        var rules = _rulesInMatchOrder;
        var themeMode = _themeMode;
        foreach (var rule in rules)
        {
            if (IsMatch(rule, line, structured, out var spans))
            {
                var (foreground, background) = rule.ResolveColors(themeMode);
                return new HighlightMatch(rule.Id, foreground, background, spans);
            }
        }

        return null;
    }

    private bool IsMatch(HighlightRule rule, string line, StructuredLogEvent? structured, out IReadOnlyList<HighlightSpan> spans)
    {
        spans = [];

        if (string.IsNullOrEmpty(rule.Pattern))
        {
            return false;
        }

        // Rules that target a structured property match against the property value, not the raw line —
        // there's no meaningful span to draw on the displayed text, so those stay whole-line only.
        var isLineTarget = string.IsNullOrEmpty(rule.TargetProperty);
        var candidate = isLineTarget ? line : StructuredFieldResolver.Resolve(structured, rule.TargetProperty!);

        if (candidate is null)
        {
            return false;
        }

        if (!rule.IsRegex)
        {
            var comparison = rule.IsCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (!candidate.Contains(rule.Pattern, comparison))
            {
                return false;
            }

            if (isLineTarget)
            {
                spans = FindKeywordSpans(line, rule.Pattern, comparison);
            }

            return true;
        }

        if (!_compiledRegexCache.TryGetValue(rule.Id, out var regex) || !IsCompiledFor(regex, rule))
        {
            var options = RegexOptions.Compiled | (rule.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            regex = new Regex(rule.Pattern, options, TimeSpan.FromSeconds(1));
            _compiledRegexCache[rule.Id] = regex;
        }

        try
        {
            var matches = regex.Matches(candidate);
            if (matches.Count == 0)
            {
                return false;
            }

            if (isLineTarget)
            {
                var list = new List<HighlightSpan>(matches.Count);
                foreach (Match m in matches)
                {
                    if (m.Length > 0)
                    {
                        list.Add(new HighlightSpan(m.Index, m.Length));
                    }
                }

                spans = list;
            }

            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsCompiledFor(Regex regex, HighlightRule rule) =>
        string.Equals(regex.ToString(), rule.Pattern, StringComparison.Ordinal)
        && (regex.Options & RegexOptions.IgnoreCase) == (rule.IsCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

    private static IReadOnlyList<HighlightSpan> FindKeywordSpans(string line, string keyword, StringComparison comparison)
    {
        var spans = new List<HighlightSpan>();
        var from = 0;
        while (from <= line.Length)
        {
            var idx = line.IndexOf(keyword, from, comparison);
            if (idx < 0)
            {
                break;
            }

            spans.Add(new HighlightSpan(idx, keyword.Length));
            from = idx + keyword.Length;
        }

        return spans;
    }
}
