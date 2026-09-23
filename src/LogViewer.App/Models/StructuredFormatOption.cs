using LogViewer.Core.Structured;

namespace LogViewer.App.Models;

/// <summary>One entry of a document's structured-format picker — the persisted id plus its display name, so
/// custom formats show their user-given name instead of a generated id.</summary>
public sealed record StructuredFormatOption(string Id, string Name)
{
    public static IReadOnlyList<StructuredFormatOption> All() =>
        [.. LogLineParsers.FormatIds.Select(id => new StructuredFormatOption(id, LogLineParsers.DisplayNameFor(id)))];
}
