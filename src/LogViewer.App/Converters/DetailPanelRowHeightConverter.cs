using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LogViewer.App.Converters;

/// <summary>Produces the structured-detail row's <see cref="GridLength"/>: zero (collapsed) when the
/// selected line has no structured detail to show, otherwise the persisted pixel height — mirrors the
/// Visibility collapse on the Border/GridSplitter so the row doesn't reserve space for a hidden panel.</summary>
public sealed class DetailPanelRowHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var height = values.Length > 0 && values[0] is double h ? h : 220d;
        var hasStructuredDetail = values.Length > 1 && values[1] is not null;
        return hasStructuredDetail ? new GridLength(height) : new GridLength(0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
