using System.Text.RegularExpressions;

namespace LogViewer.Core.Analysis;

/// <summary>Extracts the topmost stack frame ("Namespace.Type.Method") from a raw .NET exception's
/// <c>ToString()</c>-style text, used as a fallback "call site" identifier when a structured log event
/// has no configured call-site property (e.g. <c>SourceContext</c>) but does carry an exception.</summary>
public static class ExceptionFrameExtractor
{
    // Non-greedy up to the first '(' after "at " — deliberately permissive about what a frame name can
    // contain (generics, nested types, lambdas) rather than trying to enumerate every valid character.
    private static readonly Regex FramePattern = new(@"^\s*at\s+(.+?)\(", RegexOptions.Multiline | RegexOptions.Compiled);

    public static string? ExtractTopFrame(string? exceptionText)
    {
        if (string.IsNullOrEmpty(exceptionText))
        {
            return null;
        }

        var match = FramePattern.Match(exceptionText);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
}
