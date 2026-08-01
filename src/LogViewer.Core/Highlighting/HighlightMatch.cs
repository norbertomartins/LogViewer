namespace LogViewer.Core.Highlighting;

/// <summary>The highlight rule that matched a given line, if any.</summary>
public sealed record HighlightMatch(Guid RuleId, string ForegroundHex, string BackgroundHex);
