using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;
using LogViewer.Core.Structured;

namespace LogViewer.App.Converters;

/// <summary>
/// Multi-value converter that turns a <see cref="StructuredLogEvent"/> into a list of
/// <see cref="Inline"/>s suitable for a colorised <c>TextBlock</c> via
/// <c>controls:InlinesHelper.Source</c>.
/// </summary>
/// <remarks>
/// <para>Expected bindings (in order): <c>StructuredLogEvent?</c>, <c>bool colorizeEnabled</c>.</para>
/// <para>
/// When <paramref name="colorizeEnabled"/> is <c>false</c>, or when the event has no template,
/// the converter returns <c>null</c> — the XAML template then falls back to a plain
/// <c>TextBlock.Text</c> binding (the <see cref="InlinesHelper"/> no-ops on null).
/// </para>
/// </remarks>
public sealed class StructuredInlinesConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [StructuredLogEvent evt, bool colorize] || !colorize)
        {
            return null;
        }

        var segments = SerilogEventParser.SplitIntoSegments(evt);

        // If there's only one literal segment, no colorization is needed — return null so
        // the TextBlock.Text binding takes over (avoids the overhead of Inline objects).
        if (segments.Count == 1 && !segments[0].IsValue)
        {
            return null;
        }

        var inlines = new List<Inline>(segments.Count);
        foreach (var seg in segments)
        {
            if (seg.IsValue)
            {
                var run = new Run(seg.Text)
                {
                    Foreground = StructuredValueColorPalette.GetBrush(seg.PropertyName!),
                };
                inlines.Add(run);
            }
            else
            {
                inlines.Add(new Run(seg.Text));
            }
        }

        return inlines;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
