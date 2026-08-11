namespace LogViewer.Core.BlockDiff;

/// <summary>Scores how similar two blocks are, for ranking candidate matches. Built on <see cref="BlockAlignment.Align"/>
/// so scoring and the diff eventually shown to the user are always consistent with each other.</summary>
public static class BlockSimilarityScorer
{
    /// <summary>Dice coefficient over the LCS common-line count: <c>2 * commonCount / (leftCount + rightCount)</c>.
    /// 1.0 means every line matched; 0.0 means nothing in common (or either block is empty).</summary>
    public static double Score(LogBlock anchor, LogBlock candidate)
    {
        if (anchor.Lines.Count == 0 || candidate.Lines.Count == 0)
        {
            return 0d;
        }

        var alignment = BlockAlignment.Align(anchor, candidate);
        var commonCount = alignment.Count(e => e.Kind == DiffLineKind.Common);
        return 2.0 * commonCount / (anchor.Lines.Count + candidate.Lines.Count);
    }
}
