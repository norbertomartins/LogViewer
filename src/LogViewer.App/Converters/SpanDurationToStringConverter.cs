using System.Globalization;
using System.Windows.Data;
using LogViewer.Core.Structured;

namespace LogViewer.App.Converters;

/// <summary>Formats a <see cref="TraceSpanNode.Duration"/> (or any nullable <see cref="TimeSpan"/>) via
/// <see cref="SpanDurationFormatter"/>, e.g. for the Trace Tree panel's per-span duration label.</summary>
public sealed class SpanDurationToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TimeSpan duration ? SpanDurationFormatter.Format(duration) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
