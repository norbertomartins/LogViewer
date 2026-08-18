using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.App.Services;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Structured;

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

    public LogLineViewModel(long lineNumber, string text, StructuredLogEvent? structured, HighlightMatch? match, bool isBookmarked)
    {
        LineNumber = lineNumber;
        Text = text;
        Structured = structured;
        _foreground = DefaultForeground;
        _background = DefaultBackground;
        _isBookmarked = isBookmarked;
        ApplyMatch(match);
    }

    public long LineNumber { get; }

    public string Text { get; }

    /// <summary>The line parsed as a Serilog JSON event, or null when the document isn't in structured view or
    /// this particular line didn't parse (e.g. blank lines, malformed JSON) — the UI falls back to <see cref="Text"/>.</summary>
    public StructuredLogEvent? Structured { get; }

    /// <summary>The event's "ThreadId" property (from Serilog thread enrichment), or null when absent —
    /// shown as its own structured-view column.</summary>
    public string? ThreadId => StructuredFieldResolver.Resolve(Structured, "ThreadId");

    /// <summary>True when this line parsed as a structured event. Used by the structured row template to
    /// keep highlight colors (especially background) off the Bookmark/LineNumber/Timestamp/Level/ThreadId
    /// columns — a highlight background spanning the whole row can make the Level column's own foreground
    /// color (see <see cref="Converters.LevelToBrushConverter"/>) unreadable.</summary>
    public bool HasStructured => Structured is not null;

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
