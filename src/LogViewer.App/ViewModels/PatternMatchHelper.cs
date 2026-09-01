using System.Text.RegularExpressions;

namespace LogViewer.App.ViewModels;

/// <summary>Shared "does this pattern hit this line, and where" logic for the embedded regex/keyword
/// tester in the highlight and filter editors. Mirrors how <c>HighlightEngine</c> and the live text
/// filter treat a pattern (regex vs. plain substring, case sensitivity), kept dependency-free so it
/// can back both a converter and a view-model summary.</summary>
public static class PatternMatchHelper
{
    public static IReadOnlyList<(int Start, int Length)> Matches(string line, string pattern, bool isRegex, bool caseSensitive)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(line))
        {
            return [];
        }

        var result = new List<(int, int)>();

        if (isRegex)
        {
            Regex regex;
            try
            {
                regex = new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            }
            catch (ArgumentException)
            {
                return [];
            }

            foreach (Match match in regex.Matches(line))
            {
                if (match.Length > 0)
                {
                    result.Add((match.Index, match.Length));
                }
            }
        }
        else
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var cursor = 0;
            while (cursor <= line.Length - pattern.Length)
            {
                var hit = line.IndexOf(pattern, cursor, comparison);
                if (hit < 0)
                {
                    break;
                }

                result.Add((hit, pattern.Length));
                cursor = hit + pattern.Length;
            }
        }

        return result;
    }

    public static bool IsMatch(string line, string pattern, bool isRegex, bool caseSensitive) =>
        Matches(line, pattern, isRegex, caseSensitive).Count > 0;
}
