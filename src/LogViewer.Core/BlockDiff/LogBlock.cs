using LogViewer.Core.Structured;

namespace LogViewer.Core.BlockDiff;

/// <summary>One structured line within a <see cref="LogBlock"/>: its original line number, its comparison
/// <see cref="MessageSignature"/>, and the parsed event it came from.</summary>
public sealed record LogBlockLine(long LineNumber, string Signature, StructuredLogEvent Event);

/// <summary>An ordered run of structured log lines believed to belong to one logical operation, either
/// grouped by a shared correlation-id property value or by line/time proximity. <see cref="SourceDescription"/>
/// is a display label (file path or document title) identifying where the block came from.</summary>
public sealed record LogBlock(
    IReadOnlyList<LogBlockLine> Lines,
    string? CorrelationField,
    string? CorrelationValue,
    string SourceDescription);
