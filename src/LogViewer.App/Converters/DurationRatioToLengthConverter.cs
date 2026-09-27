using System.Globalization;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>
/// Pixel length of the SpanDuration column's background heat bar: <c>ratio * maxLength</c>. Bound to
/// <see cref="Models.LogLineViewModel.SpanDurationRatio"/>. <c>ConverterParameter</c> is the max bar
/// length in px (default 48); null ratio (no span duration) yields 0.
/// </summary>
public sealed class DurationRatioToLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double ratio)
        {
            return 0d;
        }

        var maxLength = 48d;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            maxLength = parsed;
        }

        return Math.Clamp(ratio, 0d, 1d) * maxLength;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
