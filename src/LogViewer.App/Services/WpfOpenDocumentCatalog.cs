using LogViewer.App.ViewModels;
using LogViewer.Core.Documents;

namespace LogViewer.App.Services;

/// <summary>Projects <see cref="MainViewModel.Documents"/> into <see cref="OpenDocumentInfo"/> for the
/// embedded MCP server — the only bridge between the WPF layer's live document state and the UI-free
/// Core/Mcp layers, so tool code never takes a WPF dependency.</summary>
public sealed class WpfOpenDocumentCatalog(MainViewModel mainViewModel) : IOpenDocumentCatalog
{
    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments()
    {
        var active = mainViewModel.ActiveDocument;

        return mainViewModel.Documents
            .Select(d => new OpenDocumentInfo(
                d.SourcePath,
                d.SearchableFilePath,
                d.DisplayTitle,
                d.Kind,
                ReferenceEquals(d, active),
                d.IsStructuredView))
            .ToList();
    }
}
