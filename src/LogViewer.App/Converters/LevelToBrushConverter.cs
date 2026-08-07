using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LogViewer.App.Converters;

/// <summary>Maps a Serilog level string (full name or common abbreviation) to a fixed, frozen brush for the
/// structured-view "Level" column. Independent of <see cref="LogViewer.Core.Highlighting.HighlightMatch"/>,
/// which still governs the row's own foreground/background.</summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    private static readonly Brush Verbose = Freeze("#808080");
    private static readonly Brush Debug = Freeze("#808080");
    private static readonly Brush Information = Freeze("#2E86C1");
    private static readonly Brush Warning = Freeze("#B7950B");
    private static readonly Brush Error = Freeze("#C0392B");
    private static readonly Brush Fatal = Freeze("#FFFFFF");
    private static readonly Brush Default = Freeze("#808080");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value as string;
        return level?.ToUpperInvariant() switch
        {
            "VERBOSE" or "VRB" => Verbose,
            "DEBUG" or "DBG" => Debug,
            "INFORMATION" or "INFO" or "INF" => Information,
            "WARNING" or "WARN" or "WRN" => Warning,
            "ERROR" or "ERR" => Error,
            "FATAL" or "FTL" => Fatal,
            _ => Default,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
