using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using LogViewer.Core.BlockDiff;

namespace LogViewer.App.Converters;

/// <summary>Maps a <see cref="DiffEntry"/> to a fixed, frozen row-background brush for the "Find Similar
/// Block" comparison window — a translucent tint distinguishing only-in-left, only-in-right, and
/// same-shape-but-different-values rows from unchanged ("Common") ones. Independent of the active theme,
/// same precedent as <see cref="LevelToBrushConverter"/>.</summary>
public sealed class DiffEntryBrushConverter : IValueConverter
{
    private static readonly Brush OnlyInLeftBrush = Freeze("#33FF6B6B");
    private static readonly Brush OnlyInRightBrush = Freeze("#334CAF50");
    private static readonly Brush ValuesDifferBrush = Freeze("#33FFC107");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DiffEntry { Kind: DiffLineKind.OnlyInLeft } => OnlyInLeftBrush,
            DiffEntry { Kind: DiffLineKind.OnlyInRight } => OnlyInRightBrush,
            DiffEntry { ValuesDiffer: true } => ValuesDifferBrush,
            _ => Brushes.Transparent,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
