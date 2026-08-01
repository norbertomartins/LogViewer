using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>True -&gt; the default control border color, False -&gt; red, for flagging invalid input inline.</summary>
public sealed class ValidityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? Brushes.Red : SystemColors.ActiveBorderBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
