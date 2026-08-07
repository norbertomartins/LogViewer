using LogViewer.Core.EventLogging;

namespace LogViewer.Core.Configuration;

/// <summary>
/// Persisted description of a tail source the user has opened, for the recent-sources list and
/// session restore. Covers all three source kinds — <see cref="Kind"/> selects which fields apply.
/// </summary>
public sealed class TailSourceSettings
{
    public TailSourceKind Kind { get; set; } = TailSourceKind.File;

    /// <summary>File path (File) or watched directory (DirectoryWatch). Unused for EventLog.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Codepage name override; null means auto-detect.</summary>
    public string? EncodingOverrideName { get; set; }

    public int? RingBufferCapacityOverride { get; set; }

    /// <summary>When true, <see cref="Path"/> is a directory and <see cref="WildcardPattern"/> selects matching files.</summary>
    public bool IsDirectoryWatch { get; set; }

    public string? WildcardPattern { get; set; }

    public bool AutoSwitchToLatestFile { get; set; }

    /// <summary>EventLog channel name (e.g. "Application"). Only meaningful when <see cref="Kind"/> is EventLog.</summary>
    public string? EventLogChannelName { get; set; }

    public List<EventLogFilterRule> EventLogFilters { get; set; } = [];

    /// <summary>Per-document tab/MDI-title-bar color override, e.g. "#3366CC". Null means default theme color.</summary>
    public string? CustomColorHex { get; set; }

    /// <summary>Per-document short glyph shown before the title. Null means no glyph.</summary>
    public string? CustomIconGlyph { get; set; }

    public double? MdiLeft { get; set; }

    public double? MdiTop { get; set; }

    public double? MdiWidth { get; set; }

    public double? MdiHeight { get; set; }

    public bool MdiIsMaximized { get; set; }

    /// <summary>Whether this document renders as structured Serilog JSON. Null means "auto-detect on open";
    /// an explicit true/false is the user's manual toggle override, persisted across restarts.</summary>
    public bool? IsStructuredView { get; set; }
}
