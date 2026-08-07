using System.Collections.Generic;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;

namespace LogViewer.App.Controls;

/// <summary>
/// Attached property that populates a <see cref="TextBlock"/>'s <see cref="TextBlock.Inlines"/>
/// from a bound <see cref="IEnumerable{T}"/> of <see cref="Inline"/> objects.
/// </summary>
/// <remarks>
/// WPF does not support direct binding to <see cref="TextBlock.Inlines"/> because it is not a
/// <see cref="DependencyProperty"/>.  This helper works around that limitation: set
/// <c>controls:InlinesHelper.Source</c> on the <see cref="TextBlock"/> and it synchronises the
/// Inlines collection whenever the value changes.  When the bound value is null the TextBlock falls
/// back to its normal <c>Text</c> binding (if any).
/// </remarks>
public static class InlinesHelper
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(IEnumerable<Inline>),
            typeof(InlinesHelper),
            new PropertyMetadata(null, OnSourceChanged));

    public static IEnumerable<Inline>? GetSource(TextBlock target) =>
        (IEnumerable<Inline>?)target.GetValue(SourceProperty);

    public static void SetSource(TextBlock target, IEnumerable<Inline>? value) =>
        target.SetValue(SourceProperty, value);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();

        if (e.NewValue is IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
            {
                textBlock.Inlines.Add(inline);
            }
        }
    }
}
