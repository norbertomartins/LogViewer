namespace LogViewer.Core.Tailing;

/// <summary>Reason a tail source was reset and needs to resume from a fresh position.</summary>
public enum TailResetReason
{
    /// <summary>The underlying file was truncated in place (common with circular/capped logs).</summary>
    Truncated,

    /// <summary>The path now points at a different underlying file (rename-and-recreate rotation).</summary>
    Rotated,

    /// <summary>The file no longer exists at the path. The source keeps polling for it to reappear.</summary>
    Deleted,
}

public sealed class TailLinesReadEventArgs(IReadOnlyList<TailLine> lines) : EventArgs
{
    public IReadOnlyList<TailLine> Lines { get; } = lines;
}

public sealed class TailSourceResetEventArgs(TailResetReason reason) : EventArgs
{
    public TailResetReason Reason { get; } = reason;
}

public sealed class TailSourceErrorEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

/// <summary>
/// A live source of appended lines — a text file or, in later phases, a Windows Event Log channel.
/// Implementations must raise <see cref="LinesRead"/> in batches (never once per line) so consumers
/// stay responsive at high line rates, and must never load an entire large source into memory.
/// </summary>
public interface ITailSource : IDisposable
{
    /// <summary>Human-readable identifier for this source, e.g. the file path.</summary>
    string DisplayName { get; }

    /// <summary>Raised on a background thread with a batch of newly available lines.</summary>
    event EventHandler<TailLinesReadEventArgs>? LinesRead;

    /// <summary>Raised when the source detects truncation, rotation, or deletion and has reset its read position.</summary>
    event EventHandler<TailSourceResetEventArgs>? SourceReset;

    /// <summary>Raised when a non-fatal error occurs while reading (source keeps trying).</summary>
    event EventHandler<TailSourceErrorEventArgs>? Error;

    /// <summary>Begins watching/reading. Safe to call once; subsequent calls are no-ops.</summary>
    void Start();

    /// <summary>Stops watching/reading without disposing the source.</summary>
    void Stop();
}
