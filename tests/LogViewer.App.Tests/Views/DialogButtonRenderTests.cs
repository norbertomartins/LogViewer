using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.App.Views.Dialogs;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.Tests.Views;

[Collection(WpfApplicationCollection.Name)]
public sealed class DialogButtonRenderTests
{
    /// <summary>
    /// Regression: text buttons with a fixed <c>Width</c> clipped their labels under the Fluent theme's padding
    /// ("📤 Expor", "✖ Canc" in the highlight preset editor), worse with the longer pt-PT strings. Renders every
    /// dialog's content offscreen at its design size with the Dark theme and checks no button label is wider than
    /// the space it was given.
    /// </summary>
    [Fact]
    public void DialogButtonLabels_AreNotClipped_UnderFluentDarkTheme() => OffscreenWpf.RunStaWithTemporaryApplication(() =>
    {
        OffscreenWpf.LoadAppResources();
        OffscreenWpf.ApplyDarkTheme();

        var dialogTypes = typeof(HighlightPresetEditorView).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(HighlightPresetEditorView).Namespace
                        && typeof(Window).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name)
            .ToList();
        Assert.NotEmpty(dialogTypes);

        var clipped = new List<string>();
        foreach (var type in dialogTypes)
        {
            var window = (Window)Activator.CreateInstance(type)!;
            if (window is HighlightPresetEditorView)
            {
                window.DataContext = new HighlightPresetEditorViewModel(HighlightPresetSeeds.CreateStarterPresets());
            }

            clipped.AddRange(ClippedButtons(window).Select(label => $"{type.Name}: \"{label}\""));
        }

        Assert.True(clipped.Count == 0, "Clipped button labels:\n" + string.Join('\n', clipped));
    });

    private static IEnumerable<string> ClippedButtons(Window window)
    {
        var width = double.IsNaN(window.Width) ? 800 : window.Width;
        var height = double.IsNaN(window.Height) ? 600 : window.Height;
        var content = (UIElement)window.Content;
        window.Content = null;
        var host = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)),
            Child = content,
            Width = width,
            Height = height,
            Padding = window.Padding,
        };
        host.Resources.MergedDictionaries.Add(window.Resources);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();

        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        OffscreenWpf.SaveIfRequested(bitmap, window.GetType().Name);

        foreach (var button in OffscreenWpf.Descendants(host).OfType<Button>())
        {
            if (button.Content is not string label || button.Visibility != Visibility.Visible || button.ActualWidth == 0)
            {
                continue;
            }

            var text = OffscreenWpf.Descendants(button).OfType<TextBlock>().FirstOrDefault();
            if (text is null)
            {
                continue;
            }

            // A clipped TextBlock keeps its full ActualWidth (WPF arranges at the desired size and clips), so compare
            // the label's natural width with the room inside the button's border and padding.
            var room = button.ActualWidth - button.Padding.Left - button.Padding.Right
                       - button.BorderThickness.Left - button.BorderThickness.Right;
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            if (text.DesiredSize.Width > room + 0.5)
            {
                yield return label;
            }
        }
    }
}
