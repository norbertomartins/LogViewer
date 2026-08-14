namespace LogViewer.Core.Documents;

/// <summary>Read-only view of the documents currently open in the running app. Lives in Core (not App)
/// so both the WPF layer and the embedded MCP server can depend on the abstraction without the MCP
/// server taking a WPF reference.</summary>
public interface IOpenDocumentCatalog
{
    IReadOnlyList<OpenDocumentInfo> GetOpenDocuments();
}
