namespace LogViewer.App.Models;

/// <summary>Where the whole-file browser should open: a 1-based line number, the first line at or after a
/// time, or (both null) the end of the file.</summary>
public sealed record FileBrowserTarget(long? LineNumber, DateTimeOffset? Time);
