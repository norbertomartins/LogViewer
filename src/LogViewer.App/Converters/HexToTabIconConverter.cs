using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>
/// Converts a "#RRGGBB" document color into a small solid-color square <see cref="ImageSource"/>, used
/// as the AvalonDock tab icon — AvalonDock's tab-header control exposes no Background/Foreground styling
/// surface, so an icon swatch is the only reliable way to surface a document's custom color in Tabbed mode.
/// Returns null (no icon) when there is no custom color.
/// </summary>
public sealed class HexToTabIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var drawing = new GeometryDrawing(
                new SolidColorBrush(color),
                new Pen(Brushes.Black, 0.5),
                new RectangleGeometry(new Rect(0, 0, 12, 12), 2, 2));

            var image = new DrawingImage(drawing);
            image.Freeze();
            return image;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
