using System.IO;
using LogViewer.App.Localization;
using LogViewer.App.ViewModels;
using LogViewer.Core.Documents;

namespace LogViewer.App.Services;

/// <summary>Projects <see cref="MainViewModel.Documents"/> into <see cref="OpenDocumentInfo"/> for the
/// embedded MCP server — the only bridge between the WPF layer's live document state and the UI-free
/// Core/Mcp layers, so tool code never takes a WPF dependency. Also applies the MCP write tools' bookmarks and notes
/// (<see cref="IDocumentAnnotationWriter"/>), which the MCP host only uses when the user allowed it.</summary>
public sealed class WpfOpenDocumentCatalog(MainViewModel mainViewModel) : IOpenDocumentCatalog, IDocumentAnnotationWriter
{
    /// <summary>MCP tools call this from Kestrel threads, but the documents (and their bookmark sets) are UI-thread
    /// state — snapshot them on the dispatcher rather than enumerating live collections from another thread.</summary>
    public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => OnUiThread(Snapshot);

    public string? AddBookmark(string sourcePath, long lineNumber) => OnUiThread(() =>
    {
        if (FindFileDocument(sourcePath) is not { } document)
        {
            return NotOpen(sourcePath);
        }

        document.EnsureBookmarked(lineNumber);
        mainViewModel.StatusMessage = Loc.Format("Vm_Mcp_AgentBookmarked", lineNumber, document.DisplayTitle);
        return null;
    });

    public string? AddNote(string sourcePath, long lineNumber, string lineText, string note) => OnUiThread(() =>
    {
        if (FindFileDocument(sourcePath) is not { } document)
        {
            return NotOpen(sourcePath);
        }

        if (!document.CanAnnotate)
        {
            return "Notes need a single-file document; this one is a merged view.";
        }

        document.AppendNote(lineNumber, lineText, note);
        mainViewModel.StatusMessage = Loc.Format("Vm_Mcp_AgentNoted", lineNumber, document.DisplayTitle);
        return null;
    });

    private static T OnUiThread<T>(Func<T> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }

    /// <summary>The open document showing <paramref name="sourcePath"/> as a file, preferring the active one.</summary>
    private TailDocumentViewModel? FindFileDocument(string sourcePath)
    {
        string full;
        try
        {
            full = Path.GetFullPath(sourcePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return mainViewModel.Documents
            .Where(d => d.SearchableFilePath is { } p && string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => ReferenceEquals(d, mainViewModel.ActiveDocument))
            .FirstOrDefault();
    }

    private static string NotOpen(string sourcePath) =>
        $"'{sourcePath}' is not open as a file document in LogViewer (see logs_list_open_documents).";

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
