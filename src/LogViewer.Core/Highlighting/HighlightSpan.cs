namespace LogViewer.Core.Highlighting;

/// <summary>A character range within a line that a highlight rule's pattern matched, for sub-string
/// (rather than whole-line) emphasis in the UI. <see cref="Start"/> is a 0-based index into the line text.</summary>
public readonly record struct HighlightSpan(int Start, int Length)
{
    public int End => Start + Length;
}
