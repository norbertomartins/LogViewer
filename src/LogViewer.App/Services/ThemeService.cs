using System.Windows;
using System.Windows.Media;
using LogViewer.Core.Configuration;
using LogViewer.Core.Theming;

namespace LogViewer.App.Services;

/// <summary>
/// Applies an <see cref="AppTheme"/> to the running app. Native chrome (windows, dialogs, menus,
/// scrollbars, combo boxes — anything WPF draws for us) follows <see cref="AppTheme.BaseMode"/> via
/// WPF's Fluent <see cref="Application.ThemeMode"/>, which is the only thing that reliably reaches
/// popup/scrollbar-level chrome; our own hand-rolled DynamicResource brushes couldn't. App-specific
/// surfaces (log area, MDI workspace) still go through DynamicResource brushes in
/// <c>Application.Resources</c> (see App.xaml), which WPF re-renders the moment those resource entries
/// change. The default (non-highlighted) log line colors go through <see cref="DefaultLogForeground"/>/
/// <see cref="DefaultLogBackground"/> — two long-lived, unfrozen brushes that <see cref="LogLineViewModel"/>
/// instances reference directly, so mutating their <see cref="SolidColorBrush.Color"/> here repaints
/// every already-rendered log line (even ones off-screen or paused) without walking the ring buffers.
/// </summary>
public sealed class ThemeService
{
    public static readonly SolidColorBrush DefaultLogForeground = new(Colors.Black);
    public static readonly SolidColorBrush DefaultLogBackground = new(Colors.White);

    public event Action? ThemeApplied;

    public IReadOnlyList<AppTheme> GetAllThemes(AppSettings settings) =>
        [.. BuiltInThemes.All, .. settings.CustomThemes];

    public AppTheme ResolveActiveTheme(AppSettings settings)
    {
        var theme = GetAllThemes(settings).FirstOrDefault(t => t.Id == settings.ActiveThemeId);
        if (theme is not null)
        {
            return theme;
        }

        // The active theme was deleted (or the id is stale) — fall back to Light rather than throw.
        settings.ActiveThemeId = BuiltInThemes.LightId;
        return BuiltInThemes.Light;
    }

    public void Apply(AppTheme theme)
    {
        var isDark = theme.BaseMode == ThemeBaseMode.Dark;

#pragma warning disable WPF0001 // ThemeMode is experimental in this SDK but is the only supported way to re-skin native chrome.
        Application.Current.ThemeMode = isDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        foreach (Window window in Application.Current.Windows)
        {
            DarkTitleBar.Apply(window, isDark);
        }

        var resources = Application.Current.Resources;
        SetBrush(resources, ThemeColorKeys.BorderColor, theme);
        SetBrush(resources, ThemeColorKeys.WorkspaceBackground, theme);
        SetBrush(resources, ThemeColorKeys.LogBackground, theme);
        SetBrush(resources, ThemeColorKeys.LogForeground, theme);
        SetSystemColorOverrides(resources, isDark);

        var titleBarDefault = isDark ? Color.FromRgb(0x3F, 0x3F, 0x46) : Color.FromRgb(0x3A, 0x6E, 0xA5);
        SetSystemBrush(resources, "Theme.TitleBarBackground", titleBarDefault);

        DefaultLogForeground.Color = ParseColor(theme, ThemeColorKeys.LogForeground, Colors.Black);
        DefaultLogBackground.Color = ParseColor(theme, ThemeColorKeys.LogBackground, Colors.White);

        ThemeApplied?.Invoke();
    }

    /// <summary>
    /// The classic Menu/MenuItem submenu popup chrome (background/border/text of a dropdown, as opposed
    /// to the menu bar itself) still resolves via the historic <see cref="SystemColors"/> DynamicResource
    /// keys rather than Fluent's Light/Dark resources — <see cref="ThemeMode"/> alone leaves it stuck
    /// light even when everything else (menu bar, dialogs, buttons) is correctly dark. Overriding these
    /// well-known keys directly is the standard WPF technique for reaching that last bit of chrome.
    /// </summary>
    private static void SetSystemColorOverrides(ResourceDictionary resources, bool isDark)
    {
        var surface = isDark ? Color.FromRgb(0x2D, 0x2D, 0x30) : SystemColors.MenuColor;
        var text = isDark ? Colors.White : SystemColors.MenuTextColor;
        var border = isDark ? Color.FromRgb(0x3F, 0x3F, 0x46) : SystemColors.ActiveBorderColor;
        var highlight = isDark ? Color.FromRgb(0x3F, 0x3F, 0x46) : SystemColors.HighlightColor;
        var highlightText = isDark ? Colors.White : SystemColors.HighlightTextColor;

        SetSystemBrush(resources, SystemColors.MenuBrushKey, surface);
        SetSystemBrush(resources, SystemColors.MenuBarBrushKey, surface);
        SetSystemBrush(resources, SystemColors.MenuTextBrushKey, text);
        SetSystemBrush(resources, SystemColors.ControlBrushKey, surface);
        SetSystemBrush(resources, SystemColors.ControlTextBrushKey, text);
        SetSystemBrush(resources, SystemColors.WindowBrushKey, surface);
        SetSystemBrush(resources, SystemColors.WindowTextBrushKey, text);
        SetSystemBrush(resources, SystemColors.ActiveBorderBrushKey, border);
        SetSystemBrush(resources, SystemColors.InactiveBorderBrushKey, border);
        SetSystemBrush(resources, SystemColors.MenuHighlightBrushKey, highlight);
        SetSystemBrush(resources, SystemColors.HighlightBrushKey, highlight);
        SetSystemBrush(resources, SystemColors.HighlightTextBrushKey, highlightText);
    }

    private static void SetSystemBrush(ResourceDictionary resources, object key, Color color)
    {
        if (resources[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
        }
        else
        {
            resources[key] = new SolidColorBrush(color);
        }
    }

    private static void SetBrush(ResourceDictionary resources, string key, AppTheme theme)
    {
        var resourceKey = "Theme." + key;
        var color = ParseColor(theme, key, Colors.Black);
        if (resources[resourceKey] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = color;
        }
        else
        {
            resources[resourceKey] = new SolidColorBrush(color);
        }
    }

    private static Color ParseColor(AppTheme theme, string key, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(theme.GetColor(key));
        }
        catch (FormatException)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
    }
}
