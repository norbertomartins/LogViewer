namespace LogViewer.Core.Documents;

/// <summary>Lets the embedded MCP server add bookmarks and notes to an open document — only ever the app's own
/// annotations, never the log file itself. Registered with the MCP host only when
/// <see cref="Configuration.McpServerSettings.AllowAnnotationWrites"/> is on. Each method returns null on success,
/// otherwise a message saying why nothing was written (document not open, line out of range, …).</summary>
public interface IDocumentAnnotationWriter
{
    /// <param name="sourcePath">The document's file path, as <see cref="OpenDocumentInfo.SearchableFilePath"/> reports it.</param>
    /// <param name="lineNumber">1-based line number in that file.</param>
    string? AddBookmark(string sourcePath, long lineNumber);

    /// <param name="lineText">The line's current text, which the note is tied to (see <c>LineAnnotationStore.HashText</c>).</param>
    /// <param name="note">Added after any note already on the line rather than replacing it.</param>
    string? AddNote(string sourcePath, long lineNumber, string lineText, string note);
}
