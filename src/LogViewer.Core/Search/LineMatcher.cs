using System.Text.RegularExpressions;

namespace LogViewer.Core.Search;

/// <summary>Builds a reusable "does this line match" predicate from what a user typed into a search box — regex or
/// plain substring, optionally case-sensitive — compiled once, since it runs over every line of a potentially huge
/// file. A regex that times out on a pathological line counts as no match rather than aborting the scan.</summary>
public static class LineMatcher
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static bool TryCreate(string? pattern, bool isRegex, bool caseSensitive, out Func<string, bool> isMatch, out string? error)
    {
        isMatch = static _ => false;
        error = null;
        if (string.IsNullOrEmpty(pattern))
        {
            error = "The search text is empty.";
            return false;
        }

        if (!isRegex)
        {
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            isMatch = line => line.Contains(pattern, comparison);
            return true;
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase), MatchTimeout);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        isMatch = line =>
        {
            try
            {
                return regex.IsMatch(line);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        };
        return true;
    }
}
