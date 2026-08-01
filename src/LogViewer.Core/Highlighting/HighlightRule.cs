using LogViewer.Core.Theming;

namespace LogViewer.Core.Highlighting;

/// <summary>A keyword or regex rule that colors matching lines. Higher <see cref="Priority"/> wins on overlap.
/// <see cref="ForegroundHex"/>/<see cref="BackgroundHex"/> are used for light-based themes; <see cref="DarkForegroundHex"/>/
/// <see cref="DarkBackgroundHex"/> optionally override them for dark-based themes — null/empty means "use the light colors".</summary>
public sealed record HighlightRule(
    Guid Id,
    string Name,
    string Pattern,
    bool IsRegex,
    bool IsCaseSensitive,
    bool IsEnabled,
    string ForegroundHex,
    string BackgroundHex,
    int Priority,
    string? DarkForegroundHex = null,
    string? DarkBackgroundHex = null)
{
    public static HighlightRule CreateDefault(string name, string pattern, bool isRegex = false) => new(
        Id: Guid.NewGuid(),
        Name: name,
        Pattern: pattern,
        IsRegex: isRegex,
        IsCaseSensitive: false,
        IsEnabled: true,
        ForegroundHex: "#000000",
        BackgroundHex: "#FFFF00",
        Priority: 0);

    /// <summary>Resolves the color pair to use for the given theme base mode.</summary>
    public (string ForegroundHex, string BackgroundHex) ResolveColors(ThemeBaseMode mode)
    {
        if (mode != ThemeBaseMode.Dark)
        {
            return (ForegroundHex, BackgroundHex);
        }

        var fg = string.IsNullOrWhiteSpace(DarkForegroundHex) ? ForegroundHex : DarkForegroundHex;
        var bg = string.IsNullOrWhiteSpace(DarkBackgroundHex) ? BackgroundHex : DarkBackgroundHex;
        return (fg, bg);
    }
}
