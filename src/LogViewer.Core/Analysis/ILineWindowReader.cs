namespace LogViewer.Core.Analysis;

public sealed record ContextLine(long LineNumber, string Text);

public sealed record LineWindowResult(long RequestedLineNumber, bool LineNumberOutOfRange, IReadOnlyList<ContextLine> Lines);

/// <summary>Reads a bounded window of raw lines around a specific line number, independent of the
/// bounded/live <c>RingLineBuffer</c> — so a requested line can be read even if it was evicted from
/// (or never reached by) the live tailing view.</summary>
public interface ILineWindowReader
{
    Task<LineWindowResult> ReadAsync(
        string sourcePath, long centerLineNumber, int linesBefore, int linesAfter, CancellationToken cancellationToken);
}
