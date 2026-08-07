using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>
/// Provides a deterministic, per-property-name brush from a hand-picked palette of vivid colors that
/// look good on both light and dark log backgrounds.  The same property name always maps to the same
/// color, so the visual pairing is stable as new lines arrive.
/// </summary>
/// <remarks>
/// Color assignment uses a simple modulo hash on the property name so it is O(1) and allocation-free
/// after the initial freeze.  The palette was chosen to mirror the hues used by the .NET SDK console
/// logger (cyan, yellow-green, magenta, orange, etc.) while keeping enough contrast for readability.
/// </remarks>
public static class StructuredValueColorPalette
{
    // Vivid hues that work on both light and dark backgrounds.
    // Each brush is frozen so it can be shared across threads without copying.
    private static readonly Brush[] Palette =
    [
        Freeze("#00BCD4"), // cyan
        Freeze("#8BC34A"), // lime green
        Freeze("#FF9800"), // orange
        Freeze("#E040FB"), // magenta / purple
        Freeze("#FFEB3B"), // yellow
        Freeze("#26C6DA"), // teal
        Freeze("#EF5350"), // coral red
        Freeze("#66BB6A"), // medium green
    ];

    /// <summary>
    /// Returns the brush associated with <paramref name="propertyName"/>.
    /// The mapping is stable: the same name always yields the same brush within a process lifetime.
    /// </summary>
    public static Brush GetBrush(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return Palette[0];
        }

        // Simple, stable hash — no need for cryptographic quality here.
        var hash = 0;
        foreach (var c in propertyName)
        {
            hash = hash * 31 + c;
        }

        return Palette[Math.Abs(hash) % Palette.Length];
    }

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
