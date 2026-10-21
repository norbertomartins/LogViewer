using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.Views.Documents;

namespace LogViewer.App.Tests.Views;

[Collection(WpfApplicationCollection.Name)]
public sealed class TimelineStripRenderTests : IDisposable
{
    private static readonly Color ErrorBarColor = Color.FromRgb(0xE5, 0x39, 0x35);

    private readonly TempDirectoryFixture _tempDir = new();

    /// <summary>
    /// Regression: under the Fluent theme the strip rendered as an empty band even though <c>VolumeBins</c> was
    /// populated — Fluent's Button style defaults to <c>HorizontalAlignment=Left</c>, so each bar Button shrank to
    /// its (width-less) Rectangles' desired width of 0px. Renders the real <see cref="TimelineStripView"/> offscreen
    /// with the Dark theme and checks the bars actually have area and paint pixels. (Only the strip: the log rows
    /// bind static brushes that another test's thread may already own.)
    /// </summary>
    [Fact]
    public void TimelineBars_RenderWithNonZeroWidth_UnderFluentDarkTheme() => OffscreenWpf.RunStaWithTemporaryApplication(() =>
    {
        OffscreenWpf.LoadAppResources();
        OffscreenWpf.ApplyDarkTheme();

        var lines = string.Join('\n', Enumerable.Range(0, 30)
            .Select(i => $"2026-02-15 09:{i:00}:00.000 [{(i % 5 == 0 ? "ERROR" : "INFO")}] event {i}")) + "\n";
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", lines));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 30);
        doc.ShowTimeline = true;
        Assert.True(doc.TimelineHasData);

        const int Width = 900, Height = 60;
        var view = new TimelineStripView { DataContext = doc };
        var host = new Border { Background = Brushes.Black, Child = view, Width = Width, Height = Height };
        host.Measure(new Size(Width, Height));
        host.Arrange(new Rect(0, 0, Width, Height));
        host.UpdateLayout();

        var strip = (Border)view.FindName("TimelineStrip");
        var buttons = OffscreenWpf.Descendants(strip).OfType<Button>().ToList();
        Assert.Equal(doc.VolumeBins.Count, buttons.Count);
        Assert.All(buttons, b => Assert.True(b.ActualWidth > 1, $"bar button is {b.ActualWidth}px wide"));

        var errorBars = OffscreenWpf.Descendants(strip).OfType<Rectangle>()
            .Where(r => r.Fill is SolidColorBrush brush && brush.Color == ErrorBarColor && r.ActualHeight > 0)
            .ToList();
        Assert.NotEmpty(errorBars);
        Assert.All(errorBars, r => Assert.True(r.ActualWidth > 0, "error bar has zero width"));

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        OffscreenWpf.SaveIfRequested(bitmap);

        var origin = strip.TranslatePoint(new Point(0, 0), host);
        var region = new Int32Rect((int)origin.X, (int)origin.Y, (int)strip.ActualWidth, (int)strip.ActualHeight);
        var pixels = new byte[region.Width * region.Height * 4];
        bitmap.CopyPixels(region, pixels, region.Width * 4, 0);
        var redPixels = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            // BGRA order; matches the #E53935 error fill.
            if (pixels[i + 2] > 200 && pixels[i + 1] < 90 && pixels[i] < 90)
            {
                redPixels++;
            }
        }

        viewModel.Dispose();
        Assert.True(redPixels > 20, $"only {redPixels} error-bar pixels painted in the timeline strip");
    });

    public void Dispose() => _tempDir.Dispose();
}
