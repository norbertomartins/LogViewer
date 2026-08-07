namespace LogViewer.Core.Structured;

/// <summary>
/// A single fragment of a rendered structured-log message, split at variable-value boundaries.
/// <para>When <see cref="PropertyName"/> is non-null the segment carries a substituted value and
/// should be rendered with a distinctive color; when it is null the segment is a literal text run
/// and should be rendered with the normal foreground.</para>
/// </summary>
public sealed record StructuredMessageSegment(string Text, string? PropertyName)
{
    /// <summary>True when this segment represents a substituted property value, false for literal text.</summary>
    public bool IsValue => PropertyName is not null;
}
