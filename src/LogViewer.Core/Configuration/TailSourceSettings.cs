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

    /// <summary>Whether this document renders as structured JSON. Null means "auto-detect on open";
    /// an explicit true/false is the user's manual toggle override, persisted across restarts.</summary>
    public bool? IsStructuredView { get; set; }

    /// <summary>The structured parser format id (<c>serilog</c>, <c>ndjson</c>, <c>logfmt</c>, <c>syslog</c>,
    /// <c>w3c</c>) the user manually picked. Null means "use the format auto-detected on open".</summary>
    public string? StructuredFormatId { get; set; }

    /// <summary>The file paths of a <see cref="TailSourceKind.MergedFiles"/> source, in the order chosen.</summary>
    public List<string> MergedPaths { get; set; } = [];

    /// <summary>Consumption mode for a <see cref="TailSourceKind.RemoteHttp"/> source: <c>Auto</c>, <c>Stream</c>, or <c>Poll</c>.</summary>
    public string? HttpMode { get; set; }

    /// <summary>Extra request headers for a <see cref="TailSourceKind.RemoteHttp"/> source, as <c>Name: Value</c> lines.</summary>
    public List<string> HttpHeaders { get; set; } = [];

    // --- Process ------------------------------------------------------------------------------------
    public string? ProcessFileName { get; set; }

    public string? ProcessArguments { get; set; }

    public bool ProcessRestartOnExit { get; set; } = true;

    // --- SSH (secrets are never persisted — password/passphrase are entered per session) -----------
    public string? SshHost { get; set; }

    public int SshPort { get; set; } = 22;

    public string? SshUsername { get; set; }

    public string? SshPrivateKeyPath { get; set; }

    public string? SshHostKeyFingerprintSha256 { get; set; }

    public bool SshAcceptAnyHostKey { get; set; }

    public string? SshCommand { get; set; }

    // --- ETW ---------------------------------------------------------------------------------------
    public string? EtwProvider { get; set; }

    public int EtwLevel { get; set; } = 4;
}
