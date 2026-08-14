using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Theming;

namespace LogViewer.Core.Configuration;

/// <summary>
/// Root persisted application settings. <see cref="SchemaVersion"/> lets <see cref="JsonSettingsStore"/>
/// run forward-compatible migrations as later phases add fields (e.g. EventLog filters).
/// </summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 5;

    public WindowModeKind DefaultWindowMode { get; set; } = WindowModeKind.Tabbed;

    public List<TailSourceSettings> RecentSources { get; set; } = [];

    public List<HighlightPreset> HighlightPresets { get; set; } = [];

    public List<ExternalToolDefinition> ExternalTools { get; set; } = [];

    public WindowLayoutSettings Layout { get; set; } = new();

    /// <summary>Id of the active theme — one of <see cref="BuiltInThemes"/> or an entry in <see cref="CustomThemes"/>.</summary>
    public string ActiveThemeId { get; set; } = BuiltInThemes.LightId;

    /// <summary>User-created themes (via duplicate-and-edit). Built-in themes are never stored here.</summary>
    public List<AppTheme> CustomThemes { get; set; } = [];

    public int RingBufferCapacity { get; set; } = 50_000;

    public int UiRefreshIntervalMs { get; set; } = 100;

    public bool RestorePreviousSessionOnStartup { get; set; } = true;

    /// <summary>When true, the UI redraw batching interval is widened automatically under a Remote Desktop session.</summary>
    public bool AutoTuneForRemoteDesktop { get; set; } = true;

    /// <summary>
    /// When true, variable values inside structured (Serilog JSON) log messages are rendered with
    /// distinct colors — one color per property name, deterministic across sessions — mimicking the
    /// .NET console logger output style.
    /// </summary>
    public bool ColorizeStructuredValues { get; set; } = true;

    /// <summary>Font size (in points) for the log line list, adjustable via Ctrl+MouseWheel over the log view.</summary>
    public double LogFontSize { get; set; } = 12;

    /// <summary>Embedded MCP server settings, letting an external AI agent analyze the logs this app is
    /// tailing. Disabled by default.</summary>
    public McpServerSettings Mcp { get; set; } = new();
}
