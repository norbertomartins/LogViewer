namespace LogViewer.Core.Highlighting;

/// <summary>Starter presets seeded into a brand-new settings file on first run. These are ordinary,
/// user-editable/deletable presets — not built-in/locked like <see cref="Theming.BuiltInThemes"/>.</summary>
public static class HighlightPresetSeeds
{
    public static List<HighlightPreset> CreateStarterPresets() =>
    [
        new HighlightPreset
        {
            Name = "Errors & Exceptions",
            IsEnabled = true,
            Rules =
            [
                HighlightRule.CreateDefault("Error", "ERROR") with { ForegroundHex = "#FFFFFF", BackgroundHex = "#C0392B" },
                HighlightRule.CreateDefault("Exception", @"\bException\b", isRegex: true) with { ForegroundHex = "#FFFFFF", BackgroundHex = "#922B21" },
                HighlightRule.CreateDefault("Warning", "WARN") with { ForegroundHex = "#000000", BackgroundHex = "#F1C40F" },
            ],
        },
    ];
}
