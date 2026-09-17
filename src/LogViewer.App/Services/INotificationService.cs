namespace LogViewer.App.Services;

/// <summary>Raises a desktop notification for a highlight-rule threshold/window alert. Abstracted so
/// <see cref="LogViewer.App.ViewModels.TailDocumentViewModel"/> stays testable without touching the
/// tray icon.</summary>
public interface INotificationService
{
    /// <summary>Best-effort, fire-and-forget — never throws, never blocks the caller.</summary>
    void Notify(string title, string message);
}
