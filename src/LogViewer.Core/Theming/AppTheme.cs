namespace LogViewer.Core.Theming;

/// <summary>
/// A named palette of "#RRGGBB"/"#AARRGGBB" colors keyed by <see cref="ThemeColorKeys"/>. Built-in
/// themes (<see cref="BuiltInThemes"/>) are re-created on every access and never persisted; only
/// user-created themes are written to <see cref="Configuration.AppSettings.CustomThemes"/>.
/// </summary>
public sealed class AppTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Theme";

    public bool IsBuiltIn { get; set; }

    /// <summary>Which Fluent native-chrome skin (Light/Dark) this theme uses.</summary>
    public ThemeBaseMode BaseMode { get; set; } = ThemeBaseMode.Light;

    public Dictionary<string, string> Colors { get; set; } = new();

    public string GetColor(string key, string fallback = "#FF000000") =>
        Colors.TryGetValue(key, out var hex) ? hex : fallback;

    /// <summary>Copies this theme's colors into a new, non-built-in theme the user can freely edit.</summary>
    public AppTheme Duplicate(string newName) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = newName,
        IsBuiltIn = false,
        BaseMode = BaseMode,
        Colors = new Dictionary<string, string>(Colors),
    };
}
