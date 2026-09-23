namespace LogViewer.Core.Configuration;

/// <summary>
/// A named, reusable combination of a document's display filters — text, minimum level, structured property,
/// correlation id and time range — saved in <see cref="AppSettings.FilterViews"/> and applicable to any open
/// document. Applying a view replaces the document's whole filter set, so a view always means the same thing.
/// Time bounds are stored as the text the user typed (e.g. <c>-15m</c>, <c>14:05</c>) and re-evaluated against
/// the target document when applied, so "the last 15 minutes" stays relative.
/// </summary>
public sealed class FilterView
{
    public string Name { get; set; } = string.Empty;

    public string? TextFilterPattern { get; set; }

    public bool TextFilterIsRegex { get; set; } = true;

    public bool TextFilterCaseSensitive { get; set; }

    public bool TextFilterExclude { get; set; }

    /// <summary>Minimum level name, or null for any level.</summary>
    public string? MinLevel { get; set; }

    public string? PropertyFilterField { get; set; }

    public string? PropertyFilterValue { get; set; }

    public string? CorrelationName { get; set; }

    public string? CorrelationValue { get; set; }

    public string? TimeFilterFromText { get; set; }

    public string? TimeFilterToText { get; set; }

    /// <summary>True when the view filters nothing (saving it would be pointless).</summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(TextFilterPattern) && string.IsNullOrEmpty(MinLevel) && string.IsNullOrEmpty(PropertyFilterValue)
        && string.IsNullOrEmpty(CorrelationValue) && string.IsNullOrWhiteSpace(TimeFilterFromText) && string.IsNullOrWhiteSpace(TimeFilterToText);
}
