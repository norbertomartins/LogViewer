namespace LogViewer.Core.Structured;

/// <summary>Resolves a highlight/search "target property" name against a parsed <see cref="StructuredLogEvent"/> —
/// shared by <see cref="Highlighting.HighlightEngine"/> and the structured-aware full-text search so both use the
/// same well-known pseudo-field names.</summary>
public static class StructuredFieldResolver
{
    public const string LevelField = "@Level";
    public const string MessageField = "@Message";
    public const string ExceptionField = "@Exception";

    /// <summary>Well-known pseudo-fields offered as suggestions in the UI, alongside free-text property names.</summary>
    public static readonly IReadOnlyList<string> WellKnownFields = [LevelField, MessageField, ExceptionField];

    public static string? Resolve(StructuredLogEvent? evt, string propertyName)
    {
        if (evt is null || string.IsNullOrEmpty(propertyName))
        {
            return null;
        }

        return propertyName switch
        {
            LevelField => evt.Level,
            MessageField => evt.RenderedMessage,
            ExceptionField => evt.Exception,
            _ => evt.Properties.TryGetValue(propertyName, out var value) ? value : null,
        };
    }
}
