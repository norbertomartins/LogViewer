namespace LogViewer.App.Models;

/// <summary>Where <see cref="ExportTarget"/>-driven export/copy output should go, and in what shape.</summary>
public enum ExportTarget
{
    /// <summary>Write plain text lines to a user-chosen file via a save dialog.</summary>
    File,

    /// <summary>Copy plain text lines to the clipboard.</summary>
    Clipboard,

    /// <summary>Copy lines to the clipboard as indented, structured JSON.</summary>
    ClipboardJson,

    /// <summary>Copy lines to the clipboard as human-readable formatted text: a structured line renders
    /// its parsed timestamp/level/message (like the structured-view columns) instead of its raw JSON
    /// text; a plain line copies unchanged.</summary>
    ClipboardFormatted,
}
