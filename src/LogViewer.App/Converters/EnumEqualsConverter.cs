using System.Globalization;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>Binds an enum value to a RadioButton's IsChecked: true when the bound enum equals
/// <c>ConverterParameter</c> (matched by name), and converts back to that parameter's enum value when checked.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string name && value.ToString() == name;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is string name ? Enum.Parse(targetType, name) : Binding.DoNothing;
}
