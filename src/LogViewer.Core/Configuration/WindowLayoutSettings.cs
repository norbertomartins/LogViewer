namespace LogViewer.Core.Configuration;

/// <summary>Persisted window chrome: last mode, main window bounds, and the AvalonDock layout XML.</summary>
public sealed class WindowLayoutSettings
{
    public WindowModeKind LastWindowMode { get; set; } = WindowModeKind.Tabbed;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool IsMaximized { get; set; }

    /// <summary>Serialized AvalonDock <c>LayoutSerializer</c> XML for dock/floating pane positions.</summary>
    public string? DockingLayoutXml { get; set; }

    /// <summary>Dedup key (see <c>MainViewModel</c>) of the document that was active when the app last closed.</summary>
    public string? ActiveSourceDedupKey { get; set; }

    /// <summary>Height (in pixels) of the structured-detail panel in <c>TailDocumentView</c>, shared across
    /// every open document and remembered across sessions.</summary>
    public double DetailPanelHeight { get; set; } = 220;

    /// <summary>True when the structured-detail panel is collapsed to just its header strip. The expanded
    /// height is kept separately in <see cref="DetailPanelHeight"/>. Shared across every open document.</summary>
    public bool IsDetailPanelCollapsed { get; set; }

    /// <summary>When true, hovering the collapsed detail panel's header temporarily expands it (without
    /// changing <see cref="IsDetailPanelCollapsed"/>); it collapses again when the mouse leaves.</summary>
    public bool DetailPanelHoverOpen { get; set; }

    /// <summary>False hides the bottom status bar (status messages and the performance readout). Shared across the window.</summary>
    public bool IsStatusBarVisible { get; set; } = true;
}
