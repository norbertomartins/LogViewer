using System.IO;

namespace LogViewer.UITests.TestUtilities;

/// <summary>
/// Moves the real <c>%LOCALAPPDATA%\LogViewer\settings.json</c> aside for the duration of a UI test
/// so the app launches with fresh defaults (no restored documents, MCP off) instead of whatever the
/// developer running these tests happens to have open, and puts it back afterwards untouched.
/// </summary>
public sealed class IsolatedSettingsFixture : IDisposable
{
    private readonly string _settingsPath;
    private readonly string? _backupPath;

    public IsolatedSettingsFixture()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogViewer");
        _settingsPath = Path.Combine(directory, "settings.json");

        if (File.Exists(_settingsPath))
        {
            _backupPath = _settingsPath + ".uitest-backup-" + Guid.NewGuid().ToString("N");
            File.Move(_settingsPath, _backupPath);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }

        if (_backupPath is not null)
        {
            File.Move(_backupPath, _settingsPath);
        }
    }
}
