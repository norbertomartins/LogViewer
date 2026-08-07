using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>Collapses when the bound value is null or an empty/whitespace string — used to hide optional
/// structured-log fields (message template, exception) that a given line didn't have.</summary>
public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
