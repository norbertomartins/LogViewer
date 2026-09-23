using LogViewer.App.ViewModels;
using LogViewer.Core.Documents;

namespace LogViewer.App.Services;

/// <summary>Projects <see cref="MainViewModel.Documents"/> into <see cref="OpenDocumentInfo"/> for the
/// embedded MCP server — the only bridge between the WPF layer's live document state and the UI-free
/// Core/Mcp layers, so tool code never takes a WPF dependency.</summary>
public sealed class WpfOpenDocumentCatalog(MainViewModel mainViewModel) : IOpenDocumentCatalog
{
    /// <summary>MCP tools call this from Kestrel threads, but the documents (and their bookmark sets) are UI-thread
    /// state — snapshot them on the dispatcher rather than enumerating live collections from another thread.</summary>
    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? Snapshot() : dispatcher.Invoke(Snapshot);
    }

    private List<OpenDocumentInfo> Snapshot()
    {
        var active = mainViewModel.ActiveDocument;

        return mainViewModel.Documents
            .Select(d => new OpenDocumentInfo(
                d.SourcePath,
                d.SearchableFilePath,
                d.DisplayTitle,
                d.Kind,
                ReferenceEquals(d, active),
                d.IsStructuredView,
                d.BookmarkedLineNumbers,
                d.Alerts.Snapshot(),
                d.Notes))
            .ToList();
    }
}
