namespace LogViewer.Core.Configuration;

/// <summary>Global master switch for the desktop-notification alert raised when a highlight rule with
/// <see cref="Highlighting.HighlightRule.AlertEnabled"/> set matches enough times within its configured
/// window (see <see cref="Highlighting.AlertWindowTracker"/>). Global because it's one app-wide
/// on/off; which rules actually alert, and their threshold/window, is configured per rule.</summary>
public sealed class NotificationAlertSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Also notify when a document sees a Warning/Error message shape it has never seen before
    /// (<see cref="Analysis.NewPatternDetector"/>). Off by default: noisy on logs with lots of unique messages.</summary>
    public bool NotifyOnNewErrorPatterns { get; set; }
}
