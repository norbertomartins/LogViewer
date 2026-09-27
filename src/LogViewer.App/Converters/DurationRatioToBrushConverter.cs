using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>
/// Fill brush for the SpanDuration column's background heat bar: interpolates from a subtle amber
/// (same #FFB300 used for the warning segment of the volume timeline) to a saturated red (#E53935,
/// its error segment) as <see cref="Models.LogLineViewModel.SpanDurationRatio"/> rises from 0 to 1, with
/// opacity rising alongside it so a fast span's bar stays nearly invisible. Null ratio yields transparent.
/// </summary>
public sealed class DurationRatioToBrushConverter : IValueConverter
{
    private static readonly Color Cool = (Color)ColorConverter.ConvertFromString("#FFB300");
    private static readonly Color Hot = (Color)ColorConverter.ConvertFromString("#E53935");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double ratio)
        {
            return Brushes.Transparent;
        }

        ratio = Math.Clamp(ratio, 0d, 1d);
        var color = Color.FromRgb(
            (byte)(Cool.R + (Hot.R - Cool.R) * ratio),
            (byte)(Cool.G + (Hot.G - Cool.G) * ratio),
            (byte)(Cool.B + (Hot.B - Cool.B) * ratio));
        var brush = new SolidColorBrush(color) { Opacity = 0.18 + 0.62 * ratio };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
