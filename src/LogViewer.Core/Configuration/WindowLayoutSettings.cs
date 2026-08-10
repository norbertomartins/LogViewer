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
}
