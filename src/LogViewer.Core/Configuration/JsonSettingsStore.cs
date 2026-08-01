using System.Text.Json;

namespace LogViewer.Core.Configuration;

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON at a given file path. The path is injected rather than
/// hardcoded so tests can point it at a temp directory instead of the real <c>%LOCALAPPDATA%</c>.
/// </summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public static JsonSettingsStore CreateDefault()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogViewer");
        return new JsonSettingsStore(Path.Combine(directory, "settings.json"));
    }

    public AppSettings Load()
    {
        if (!File.Exists(filePath))
        {
            return new AppSettings();
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return Migrate(settings ?? new AppSettings());
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        File.WriteAllText(filePath, json);
    }

    private static AppSettings Migrate(AppSettings settings)
    {
        // v1 -> v2: RecentSources entries predate TailSourceKind and default to File (the only kind
        // v1 ever wrote), so no field-level migration is needed — new fields simply default sensibly.
        // v2 -> v3: ActiveThemeId/CustomThemes are new; AppSettings' field initializers already give
        // pre-v3 files a sensible default (built-in Light theme, no custom themes).
        settings.SchemaVersion = 3;
        return settings;
    }
}
