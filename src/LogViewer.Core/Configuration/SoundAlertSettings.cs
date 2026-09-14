namespace LogViewer.Core.Configuration;

/// <summary>Global settings for the audible alert played when an Error/Fatal line arrives in a document
/// that has sound alerts enabled (see <see cref="TailSourceSettings.SoundAlertsEnabled"/>). Global because
/// the sound itself is one app-wide choice; each window separately opts in or out via its own toggle.</summary>
public sealed class SoundAlertSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Path to a custom .wav file to play; null plays the system exclamation sound instead.</summary>
    public string? CustomSoundFilePath { get; set; }
}
