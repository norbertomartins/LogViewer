using System.IO;

namespace LogViewer.UITests.TestUtilities;

/// <summary>
/// Moves the real <c>%LOCALAPPDATA%\LogViewer\settings.json</c> (and <c>annotations.json</c>, the line notes)
/// aside for the duration of a UI test so the app launches with fresh defaults (no restored documents, MCP off)
/// instead of whatever the developer running these tests happens to have open, and puts them back afterwards
/// untouched.
/// </summary>
public sealed class IsolatedSettingsFixture : IDisposable
{
    private readonly string _settingsPath;
    private readonly string? _backupPath;
    private readonly string _annotationsPath;
    private readonly string? _annotationsBackupPath;

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

        _annotationsPath = Path.Combine(directory, "annotations.json");
        if (File.Exists(_annotationsPath))
        {
            _annotationsBackupPath = _annotationsPath + ".uitest-backup-" + Guid.NewGuid().ToString("N");
            File.Move(_annotationsPath, _annotationsBackupPath);
        }
    }

    /// <summary>A settings.json that restores a single file-backed document on startup (MCP off), so a UI
    /// test can assert against a window that already has a log open without driving the native file dialog.</summary>
    public static string RestoringFile(string absoluteFilePath) =>
        $$"""
        {
          "SchemaVersion": 7,
          "RestorePreviousSessionOnStartup": true,
          "Mcp": { "Enabled": false },
          "RecentSources": [ { "Kind": 0, "Path": {{System.Text.Json.JsonSerializer.Serialize(absoluteFilePath)}} } ]
        }
        """;

    /// <summary>Same as <see cref="RestoringFile"/>, but with a small <c>RingBufferCapacity</c> — so a UI
    /// test that appends far more lines than the capacity can exercise ring-buffer eviction without
    /// having to write an unreasonably large file — plus an "ERROR"/"WARN" keyword highlight preset (the
    /// same colors the app seeds a brand-new install with), so highlight-under-eviction tests have real
    /// rules to match against.</summary>
    public static string RestoringFileWithSmallBufferAndHighlights(string absoluteFilePath, int ringBufferCapacity) =>
        $$"""
        {
          "SchemaVersion": 7,
          "RestorePreviousSessionOnStartup": true,
          "Mcp": { "Enabled": false },
          "RingBufferCapacity": {{ringBufferCapacity}},
          "RecentSources": [ { "Kind": 0, "Path": {{System.Text.Json.JsonSerializer.Serialize(absoluteFilePath)}} } ],
          "HighlightPresets": [
            {
              "Id": "8f4a2f2a-1b1a-4b3a-9b1a-000000000001",
              "Name": "Errors & Exceptions",
              "IsEnabled": true,
              "Rules": [
                {
                  "Id": "8f4a2f2a-1b1a-4b3a-9b1a-000000000002",
                  "Name": "Error",
                  "Pattern": "ERROR",
                  "IsRegex": false,
                  "IsCaseSensitive": false,
                  "IsEnabled": true,
                  "ForegroundHex": "#FFFFFF",
                  "BackgroundHex": "#C0392B"
                },
                {
                  "Id": "8f4a2f2a-1b1a-4b3a-9b1a-000000000003",
                  "Name": "Warning",
                  "Pattern": "WARN",
                  "IsRegex": false,
                  "IsCaseSensitive": false,
                  "IsEnabled": true,
                  "ForegroundHex": "#000000",
                  "BackgroundHex": "#F1C40F"
                }
              ]
            }
          ]
        }
        """;

    /// <summary>Same as <see cref="RestoringFile"/>, but with the built-in Dark theme active — so a UI
    /// test can assert against actually-rendered dark-palette colors without driving the Settings dialog.</summary>
    public static string RestoringFileInDarkTheme(string absoluteFilePath) =>
        $$"""
        {
          "SchemaVersion": 7,
          "RestorePreviousSessionOnStartup": true,
          "Mcp": { "Enabled": false },
          "ActiveThemeId": "builtin-dark",
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

        if (File.Exists(_annotationsPath))
        {
            File.Delete(_annotationsPath);
        }

        if (_annotationsBackupPath is not null)
        {
            File.Move(_annotationsBackupPath, _annotationsPath);
        }
    }
}
