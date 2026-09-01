namespace LogViewer.Core.Configuration;

/// <summary>
/// A named, switchable snapshot of a working set: the documents that were open (each as a
/// <see cref="TailSourceSettings"/>, the same shape session-restore consumes), the window mode, the
/// AvalonDock layout, and which document was active. Lets a user keep e.g. a "prod incident" set and a
/// "local dev" set and jump between them, independent of the single auto-persisted last session.
/// </summary>
public sealed class SessionProfile
{
    public string Name { get; set; } = string.Empty;

    public List<TailSourceSettings> Sources { get; set; } = [];

    public WindowModeKind WindowMode { get; set; } = WindowModeKind.Tabbed;

    /// <summary>Serialized AvalonDock layout XML captured when the profile was saved, or null.</summary>
    public string? DockingLayoutXml { get; set; }

    /// <summary>Dedup key of the document that was active when the profile was saved.</summary>
    public string? ActiveSourceDedupKey { get; set; }

    public DateTimeOffset SavedAtUtc { get; set; }
}
