using System.Globalization;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>Inverts a bool, e.g. for disabling editable controls when a "read-only" flag is true.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value!;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : value!;
}
