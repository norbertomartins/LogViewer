namespace LogViewer.Core.Theming;

/// <summary>The two themes LogViewer ships with. Never persisted — always re-created from these
/// constants so a future code change to the palette applies even to settings files written earlier.</summary>
public static class BuiltInThemes
{
    public const string LightId = "builtin-light";
    public const string DarkId = "builtin-dark";

    public static AppTheme Light => new()
    {
        Id = LightId,
        Name = "Light",
        IsBuiltIn = true,
        BaseMode = ThemeBaseMode.Light,
        Colors = new Dictionary<string, string>
        {
            [ThemeColorKeys.BorderColor] = "#FFACACAC",
            [ThemeColorKeys.WorkspaceBackground] = "#FFDDE3EA",
            [ThemeColorKeys.LogBackground] = "#FFFFFFFF",
            [ThemeColorKeys.LogForeground] = "#FF000000",
        },
    };

    public static AppTheme Dark => new()
    {
        Id = DarkId,
        Name = "Dark",
        IsBuiltIn = true,
        BaseMode = ThemeBaseMode.Dark,
        Colors = new Dictionary<string, string>
        {
            [ThemeColorKeys.BorderColor] = "#FF3F3F46",
            [ThemeColorKeys.WorkspaceBackground] = "#FF181818",
            [ThemeColorKeys.LogBackground] = "#FF1B1B1B",
            [ThemeColorKeys.LogForeground] = "#FFD4D4D4",
        },
    };

    public static IReadOnlyList<AppTheme> All => [Light, Dark];
}
