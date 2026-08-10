using System.Globalization;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>Converts a structured log event's <see cref="DateTimeOffset"/> timestamp to the machine's
/// local time zone before formatting it, so the displayed clock time reflects local time (and daylight
/// saving, when applicable) instead of whatever offset the log line was written with.</summary>
public sealed class TimestampToLocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTimeOffset timestamp)
        {
            return null;
        }

        var format = parameter as string ?? "HH:mm:ss.fff";
        return timestamp.ToLocalTime().ToString(format, culture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
