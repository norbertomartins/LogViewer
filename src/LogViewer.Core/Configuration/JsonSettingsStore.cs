using System.Text.Json;
using LogViewer.Core.Highlighting;

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
            var fresh = new AppSettings();
            fresh.HighlightPresets.AddRange(HighlightPresetSeeds.CreateStarterPresets());
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            return Migrate(settings ?? new AppSettings(), json);
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

    private static AppSettings Migrate(AppSettings settings, string rawJson)
    {
        // v1 -> v2: RecentSources entries predate TailSourceKind and default to File (the only kind
        // v1 ever wrote), so no field-level migration is needed — new fields simply default sensibly.
        // v2 -> v3: ActiveThemeId/CustomThemes are new; AppSettings' field initializers already give
        // pre-v3 files a sensible default (built-in Light theme, no custom themes).
        // v3 -> v4: the flat, Priority-ranked GlobalHighlightRules list was replaced by ordered,
        // independently-toggleable HighlightPresets. HighlightRule no longer has a Priority property, so
        // it has to be recovered from the raw JSON (not the already-deserialized AppSettings) before it's
        // lost, then baked into list order as a single preset.
        if (settings.SchemaVersion < 4)
        {
            var migrated = MigrateLegacyHighlightRules(rawJson);
            if (migrated is not null)
            {
                settings.HighlightPresets.Insert(0, migrated);
            }
        }

        // v4 -> v5: Mcp is new; AppSettings' field initializer already gives pre-v5 files a sensible
        // disabled-by-default McpServerSettings, so no field-level migration is needed.
        // v5 -> v6: SessionProfiles is new (plus per-document filter fields on TailSourceSettings); the
        // field initializers already give pre-v6 files an empty profile list and inert filters.
        // v6 -> v7: Language is new; the field initializer already gives pre-v7 files "en" (neutral
        // resources), which is exactly the English-only behavior they had before.
        settings.SchemaVersion = 7;
        return settings;
    }

    private sealed record LegacyHighlightRule(
        Guid Id, string Name, string Pattern, bool IsRegex, bool IsCaseSensitive, bool IsEnabled,
        string ForegroundHex, string BackgroundHex, int Priority,
        string? DarkForegroundHex = null, string? DarkBackgroundHex = null);

    private static HighlightPreset? MigrateLegacyHighlightRules(string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (!doc.RootElement.TryGetProperty("GlobalHighlightRules", out var element) || element.GetArrayLength() == 0)
        {
            return null;
        }

        var legacy = JsonSerializer.Deserialize<List<LegacyHighlightRule>>(element.GetRawText(), SerializerOptions) ?? [];
        if (legacy.Count == 0)
        {
            return null;
        }

        var rules = legacy
            .OrderByDescending(r => r.Priority)
            .Select(r => new HighlightRule(r.Id, r.Name, r.Pattern, r.IsRegex, r.IsCaseSensitive, r.IsEnabled,
                r.ForegroundHex, r.BackgroundHex, r.DarkForegroundHex, r.DarkBackgroundHex))
            .ToList();

        return new HighlightPreset { Name = "My Highlights", IsEnabled = true, Rules = rules };
    }
}
