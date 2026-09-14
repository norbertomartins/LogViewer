namespace LogViewer.App.Services;

/// <summary>Plays the audible alert for an Error/Fatal log line. Abstracted so
/// <see cref="LogViewer.App.ViewModels.TailDocumentViewModel"/> stays testable without touching audio.</summary>
public interface ISoundAlertPlayer
{
    /// <summary>Plays <paramref name="customSoundFilePath"/> if set and it exists, otherwise the system
    /// exclamation sound. Fire-and-forget — never throws, never blocks the caller.</summary>
    void PlayAlert(string? customSoundFilePath);
}
