namespace LogViewer.Core.Highlighting;

/// <summary>The highlight rule that matched a given line, if any.</summary>
/// <param name="RuleId">The winning rule's id.</param>
/// <param name="ForegroundHex">Whole-line foreground color.</param>
/// <param name="BackgroundHex">Whole-line background color.</param>
/// <param name="Spans">The character ranges of the line text the pattern matched, for sub-string emphasis.
/// Empty when the rule targets a structured property (the match isn't in the raw line) or spans weren't computed.</param>
public sealed record HighlightMatch(
    Guid RuleId,
    string ForegroundHex,
    string BackgroundHex,
    IReadOnlyList<HighlightSpan> Spans)
{
    public HighlightMatch(Guid ruleId, string foregroundHex, string backgroundHex)
        : this(ruleId, foregroundHex, backgroundHex, [])
    {
    }
}
