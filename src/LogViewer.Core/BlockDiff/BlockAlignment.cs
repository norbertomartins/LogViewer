namespace LogViewer.Core.BlockDiff;

public enum DiffLineKind
{
    Common,
    OnlyInLeft,
    OnlyInRight,
}

/// <summary>One aligned row of a block diff. <see cref="ValuesDiffer"/> is only meaningful for <see cref="DiffLineKind.Common"/>
/// rows — true when the two sides' shared property keys or rendered messages don't match, even though the line's
/// "shape" (signature) is the same.</summary>
public sealed record DiffEntry(DiffLineKind Kind, LogBlockLine? Left, LogBlockLine? Right, bool ValuesDiffer);

/// <summary>Aligns two <see cref="LogBlock"/>s by <see cref="LogBlockLine.Signature"/> equality, producing an
/// ordered line-level diff. Shared by <see cref="BlockSimilarityScorer"/> (scoring) and the comparison UI
/// (display), so what was scored and what's shown never drift apart.</summary>
public static class BlockAlignment
{
    // Blocks are expected to be small (tens to low hundreds of lines), so the O(n*m) LCS DP table is
    // cheap. Guards against pathological inputs by falling back to a cheap greedy alignment instead.
    private const int MaxDpCells = 250_000;

    public static IReadOnlyList<DiffEntry> Align(LogBlock left, LogBlock right)
    {
        var leftLines = left.Lines;
        var rightLines = right.Lines;

        return (long)leftLines.Count * rightLines.Count <= MaxDpCells
            ? AlignByLcs(leftLines, rightLines)
            : AlignGreedy(leftLines, rightLines);
    }

    private static IReadOnlyList<DiffEntry> AlignByLcs(IReadOnlyList<LogBlockLine> left, IReadOnlyList<LogBlockLine> right)
    {
        var n = left.Count;
        var m = right.Count;
        var dp = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = left[i].Signature == right[j].Signature
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var entries = new List<DiffEntry>();
        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (left[x].Signature == right[y].Signature)
            {
                entries.Add(new DiffEntry(DiffLineKind.Common, left[x], right[y], ValuesDiffer(left[x], right[y])));
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
            {
                entries.Add(new DiffEntry(DiffLineKind.OnlyInLeft, left[x], null, false));
                x++;
            }
            else
            {
                entries.Add(new DiffEntry(DiffLineKind.OnlyInRight, null, right[y], false));
                y++;
            }
        }

        for (; x < n; x++)
        {
            entries.Add(new DiffEntry(DiffLineKind.OnlyInLeft, left[x], null, false));
        }

        for (; y < m; y++)
        {
            entries.Add(new DiffEntry(DiffLineKind.OnlyInRight, null, right[y], false));
        }

        return entries;
    }

    /// <summary>Cheap fallback for pathologically large blocks: walk both sequences, greedily matching the next
    /// equal signature within a small lookahead window instead of full O(n*m) DP.</summary>
    private static IReadOnlyList<DiffEntry> AlignGreedy(IReadOnlyList<LogBlockLine> left, IReadOnlyList<LogBlockLine> right)
    {
        const int lookahead = 25;
        var entries = new List<DiffEntry>();
        var x = 0;
        var y = 0;

        while (x < left.Count && y < right.Count)
        {
            if (left[x].Signature == right[y].Signature)
            {
                entries.Add(new DiffEntry(DiffLineKind.Common, left[x], right[y], ValuesDiffer(left[x], right[y])));
                x++;
                y++;
                continue;
            }

            var matchInRight = FindWithin(right, y, lookahead, left[x].Signature);
            var matchInLeft = FindWithin(left, x, lookahead, right[y].Signature);

            if (matchInRight is { } ry && (matchInLeft is not { } lx || ry - y <= lx - x))
            {
                for (var k = y; k < ry; k++)
                {
                    entries.Add(new DiffEntry(DiffLineKind.OnlyInRight, null, right[k], false));
                }

                y = ry;
            }
            else if (matchInLeft is { } lx2)
            {
                for (var k = x; k < lx2; k++)
                {
                    entries.Add(new DiffEntry(DiffLineKind.OnlyInLeft, left[k], null, false));
                }

                x = lx2;
            }
            else
            {
                entries.Add(new DiffEntry(DiffLineKind.OnlyInLeft, left[x], null, false));
                entries.Add(new DiffEntry(DiffLineKind.OnlyInRight, null, right[y], false));
                x++;
                y++;
            }
        }

        for (; x < left.Count; x++)
        {
            entries.Add(new DiffEntry(DiffLineKind.OnlyInLeft, left[x], null, false));
        }

        for (; y < right.Count; y++)
        {
            entries.Add(new DiffEntry(DiffLineKind.OnlyInRight, null, right[y], false));
        }

        return entries;
    }

    private static int? FindWithin(IReadOnlyList<LogBlockLine> lines, int start, int window, string signature)
    {
        var end = Math.Min(lines.Count, start + window);
        for (var i = start; i < end; i++)
        {
            if (lines[i].Signature == signature)
            {
                return i;
            }
        }

        return null;
    }

    private static bool ValuesDiffer(LogBlockLine left, LogBlockLine right)
    {
        foreach (var key in left.Event.Properties.Keys)
        {
            if (right.Event.Properties.TryGetValue(key, out var rightValue)
                && !string.Equals(left.Event.Properties[key], rightValue, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return !string.Equals(left.Event.RenderedMessage, right.Event.RenderedMessage, StringComparison.Ordinal);
    }
}
