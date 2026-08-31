using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.Converters;

/// <summary>
/// Turns a plain log line plus its <see cref="HighlightSpan"/> list into <see cref="Inline"/>s so the
/// exact matched sub-string(s) render bold + underlined, on top of the row's whole-line highlight color.
/// </summary>
/// <remarks>
/// Expected bindings (in order): <c>string text</c>, <c>IReadOnlyList&lt;HighlightSpan&gt; spans</c>,
/// <c>bool enabled</c>. Returns <c>null</c> when disabled or there are no spans, so the <c>TextBlock.Text</c>
/// binding takes over via <c>InlinesHelper</c>.
/// </remarks>
public sealed class HighlightSpanInlinesConverter : IMultiValueConverter
{
    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [string text, IReadOnlyList<HighlightSpan> spans, bool enabled]
            || !enabled || spans.Count == 0 || text.Length == 0)
        {
            return null;
        }

        var ordered = spans
            .Where(s => s.Start >= 0 && s.Length > 0 && s.Start < text.Length)
            .OrderBy(s => s.Start)
            .ToList();

        if (ordered.Count == 0)
        {
            return null;
        }

        var inlines = new List<Inline>();
        var cursor = 0;

        foreach (var span in ordered)
        {
            var start = Math.Max(span.Start, cursor);
            var end = Math.Min(span.End, text.Length);
            if (end <= start)
            {
                continue;
            }

            if (start > cursor)
            {
                inlines.Add(new Run(text[cursor..start]));
            }

            inlines.Add(new Run(text[start..end])
            {
                FontWeight = FontWeights.Bold,
                TextDecorations = TextDecorations.Underline,
            });

            cursor = end;
        }

        if (cursor < text.Length)
        {
            inlines.Add(new Run(text[cursor..]));
        }

        return inlines;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
