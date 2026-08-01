using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.App.Services;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.Models;

/// <summary>The UI-bound representation of one tailed line: text plus its resolved highlight brushes and bookmark state.</summary>
public sealed partial class LogLineViewModel : ObservableObject
{
    private static readonly Dictionary<string, Brush> BrushCache = new();

    // Shared, unfrozen brushes owned by ThemeService: every non-highlighted line points at the same
    // instances, so a theme switch repaints them all in place without touching each line's own state.
    private static readonly Brush DefaultForeground = ThemeService.DefaultLogForeground;
    private static readonly Brush DefaultBackground = ThemeService.DefaultLogBackground;

    [ObservableProperty]
    private Brush _foreground;

    [ObservableProperty]
    private Brush _background;

    [ObservableProperty]
    private bool _isBookmarked;

    public LogLineViewModel(long lineNumber, string text, HighlightMatch? match, bool isBookmarked)
    {
        LineNumber = lineNumber;
        Text = text;
        _foreground = DefaultForeground;
        _background = DefaultBackground;
        _isBookmarked = isBookmarked;
        ApplyMatch(match);
    }

    public long LineNumber { get; }

    public string Text { get; }

    /// <summary>Re-resolves this line's colors against a (possibly new) highlight match — used both at
    /// append time and to live-recolor already-displayed lines when highlight rules or the theme change.</summary>
    public void ApplyMatch(HighlightMatch? match)
    {
        Foreground = match is null ? DefaultForeground : ResolveBrush(match.ForegroundHex, DefaultForeground);
        Background = match is null ? DefaultBackground : ResolveBrush(match.BackgroundHex, DefaultBackground);
    }

    private static Brush ResolveBrush(string hex, Brush fallback)
    {
        if (BrushCache.TryGetValue(hex, out var cached))
        {
            return cached;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            BrushCache[hex] = brush;
            return brush;
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
