namespace LogViewer.Core.Configuration;

/// <summary>Settings for the embedded MCP (Model Context Protocol) server, which lets an external AI
/// agent query the logs this app is tailing. Strictly opt-in — <see cref="Enabled"/> defaults to false
/// because turning it on opens a local network listener.</summary>
public sealed class McpServerSettings
{
    public bool Enabled { get; set; }

    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 38173;

    public int MaxResultsPerCall { get; set; } = 200;

    public int MaxLineTextLength { get; set; } = 4000;

    public bool RequireApiKeyHeader { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>Separate opt-in (off by default) for the tools that let the agent add bookmarks and notes. They only
    /// touch the app's own annotations — log files are never modified.</summary>
    public bool AllowAnnotationWrites { get; set; }
}
