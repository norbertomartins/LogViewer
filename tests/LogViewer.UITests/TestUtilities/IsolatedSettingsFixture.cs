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

    public IsolatedSettingsFixture(string? initialSettingsJson = null)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogViewer");
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");

        if (File.Exists(_settingsPath))
        {
            _backupPath = _settingsPath + ".uitest-backup-" + Guid.NewGuid().ToString("N");
            File.Move(_settingsPath, _backupPath);
        }

        if (initialSettingsJson is not null)
        {
            File.WriteAllText(_settingsPath, initialSettingsJson);
        }
    }

    /// <summary>A settings.json that restores a single file-backed document on startup (MCP off), so a UI
    /// test can assert against a window that already has a log open without driving the native file dialog.</summary>
    public static string RestoringFile(string absoluteFilePath) =>
        $$"""
        {
          "SchemaVersion": 5,
          "RestorePreviousSessionOnStartup": true,
          "Mcp": { "Enabled": false },
          "RecentSources": [ { "Kind": 0, "Path": {{System.Text.Json.JsonSerializer.Serialize(absoluteFilePath)}} } ]
        }
        """;

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
