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
        // v7 -> v8: SoundAlerts is new (plus the per-document SoundAlertsEnabled override); the field
        // initializers already give pre-v8 files an enabled-by-default global setting and "use the
        // global default" per document, so no field-level migration is needed.
        // v8 -> v9: NotificationAlerts is new (plus AlertEnabled/AlertThresholdCount/AlertWindowSeconds
        // on HighlightRule); the field initializers already give pre-v9 files an enabled-by-default
        // global setting and alerts disabled on every existing rule, so no field-level migration is needed.
        // v9 -> v10: NotifyOnFileSwitch is new; the field initializer already gives pre-v10 files an
        // enabled-by-default global setting, so no field-level migration is needed.
        // v10 -> v11: CustomLogFormats is new; the field initializer already gives pre-v11 files an empty
        // list (built-in formats only), so no field-level migration is needed.
        // v11 -> v12: NotificationAlerts.NotifyOnNewErrorPatterns is new; its initializer (false) keeps pre-v12
        // behavior (no new-pattern notifications), so no field-level migration is needed.
        // v12 -> v13: FilterViews is new, and TailSourceSettings gained persisted time-range and correlation
        // filters; initializers give pre-v13 files no views and inactive filters, so no field-level migration.
        // v13 -> v14: TailSourceSettings.ArchiveEntry is new (zip entry of a File source); null keeps pre-v14
        // behavior (open the file itself), so no field-level migration is needed.
        // v14 -> v15: Mcp.AllowAnnotationWrites is new; its initializer (false) keeps pre-v15 behavior (read-only MCP
        // tools), so no field-level migration is needed.
        // v15 -> v16: CustomColors is new (the color picker's remembered custom colors); the initializer gives pre-v16
        // files an empty list, so no field-level migration is needed.
        settings.SchemaVersion = 16;
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
