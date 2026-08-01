namespace LogViewer.Core.Tailing;

/// <summary>A single line read from a tail source, with its position in the underlying stream.</summary>
public sealed record TailLine(long LineNumber, long ByteOffset, string Text, DateTimeOffset ReadAtUtc);
