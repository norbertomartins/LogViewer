using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Converters;

/// <summary>
/// Renders one sample line for the embedded pattern tester: matched sub-strings come back bold with a
/// translucent accent background, the rest as plain runs. Returns <c>null</c> (falls back to plain
/// <c>TextBlock.Text</c>) when there is nothing to mark.
/// </summary>
/// <remarks>Bindings, in order: <c>string line</c>, <c>string pattern</c>, <c>bool isRegex</c>,
/// <c>bool caseSensitive</c>.</remarks>
public sealed class RegexTestInlinesConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string line, string pattern, bool isRegex, bool caseSensitive])
        {
            return null;
        }

        var matches = PatternMatchHelper.Matches(line, pattern, isRegex, caseSensitive);
        if (matches.Count == 0)
        {
            return null;
        }

        var highlight = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD5, 0x4F));
        highlight.Freeze();

        var inlines = new List<Inline>();
        var cursor = 0;
        foreach (var (start, length) in matches.OrderBy(m => m.Start))
        {
            var clampedStart = Math.Max(start, cursor);
            var end = Math.Min(start + length, line.Length);
            if (end <= clampedStart)
            {
                continue;
            }

            if (clampedStart > cursor)
            {
                inlines.Add(new Run(line[cursor..clampedStart]));
            }

            inlines.Add(new Run(line[clampedStart..end]) { FontWeight = FontWeights.Bold, Background = highlight });
            cursor = end;
        }

        if (cursor < line.Length)
        {
            inlines.Add(new Run(line[cursor..]));
        }

        return inlines;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
