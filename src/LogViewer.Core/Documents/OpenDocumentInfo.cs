using LogViewer.Core.Annotations;
using LogViewer.Core.Configuration;
using LogViewer.Core.Highlighting;

namespace LogViewer.Core.Documents;

/// <summary>Describes one currently open/tailed document in the running app, projected from the WPF
/// layer's document collection so MCP tools can discover what's available without a WPF dependency.
/// <paramref name="BookmarkedLineNumbers"/> are the user's bookmarks (absolute line numbers, ascending);
/// <paramref name="RecentAlerts"/> the alerts the document raised, oldest first; <paramref name="Notes"/> the user's
/// line notes (by line number).</summary>
public sealed record OpenDocumentInfo(
    string SourcePath,
    string? SearchableFilePath,
    string Title,
    TailSourceKind Kind,
    bool IsActive,
    bool IsStructuredView,
    IReadOnlyList<long>? BookmarkedLineNumbers = null,
    IReadOnlyList<AlertRecord>? RecentAlerts = null,
    IReadOnlyList<LineAnnotation>? Notes = null);
