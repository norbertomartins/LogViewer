using System.IO;
using System.Media;

namespace LogViewer.App.Services;

public sealed class SoundAlertPlayer : ISoundAlertPlayer
{
    // Cache SoundPlayer instances (and their pre-loaded audio buffer) per path so a repeated alert
    // doesn't re-read and re-decode the .wav file from disk every time it fires.
    private readonly Dictionary<string, SoundPlayer> _customPlayers = new(StringComparer.OrdinalIgnoreCase);

    public void PlayAlert(string? customSoundFilePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(customSoundFilePath) && File.Exists(customSoundFilePath))
            {
                GetOrCreatePlayer(customSoundFilePath).Play();
            }
            else
            {
                SystemSounds.Exclamation.Play();
            }
        }
        catch (Exception)
        {
            // Best-effort alert — a bad/locked sound file must never break log tailing.
        }
    }

    private SoundPlayer GetOrCreatePlayer(string path)
    {
        if (_customPlayers.TryGetValue(path, out var existing))
        {
            return existing;
        }

        var player = new SoundPlayer(path);
        player.LoadAsync();
        _customPlayers[path] = player;
        return player;
    }
}
