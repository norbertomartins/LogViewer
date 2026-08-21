using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>
/// Converts a document's "#RRGGBB" <c>CustomColorHex</c> into its title-bar brush: the parsed custom
/// color when set, otherwise the live "Theme.TitleBarBackground" resource — returned as the SAME
/// <see cref="SolidColorBrush"/> instance <see cref="Services.ThemeService"/> mutates in place, so a
/// theme switch repaints the title bar immediately without waiting for this binding to re-evaluate.
/// </summary>
public sealed class CustomColorOrTitleBarBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch (FormatException)
            {
                // Fall through to the theme default below.
            }
        }

        return Application.Current.Resources["Theme.TitleBarBackground"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
