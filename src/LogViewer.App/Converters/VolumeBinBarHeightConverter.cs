using System.Globalization;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>
/// Height (in px) of one stacked segment of a volume-timeline bar: <c>count / maxBinTotal * maxHeight</c>.
/// Bindings (in order): <c>int segmentCount</c>, <c>int maxBinTotal</c>. <c>ConverterParameter</c> is the
/// max bar height in px (default 44).
/// </summary>
public sealed class VolumeBinBarHeightConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [int count, int max] || max <= 0 || count <= 0)
        {
            return 0d;
        }

        var maxHeight = 44d;
        if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            maxHeight = parsed;
        }

        return Math.Max(1d, Math.Min(1d, (double)count / max) * maxHeight);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
