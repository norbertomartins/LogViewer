using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>True -&gt; Collapsed, False -&gt; Visible. Used for "show this only when the flag is false" bindings.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
