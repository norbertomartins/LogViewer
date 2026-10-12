using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.Views.Documents;
using LogViewer.Core.Theming;

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
    public void TimelineBars_RenderWithNonZeroWidth_UnderFluentDarkTheme() => RunStaWithTemporaryApplication(() =>
    {
        LoadAppResources();
        ApplyDarkTheme();

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
        var buttons = Descendants(strip).OfType<Button>().ToList();
        Assert.Equal(doc.VolumeBins.Count, buttons.Count);
        Assert.All(buttons, b => Assert.True(b.ActualWidth > 1, $"bar button is {b.ActualWidth}px wide"));

        var errorBars = Descendants(strip).OfType<Rectangle>()
            .Where(r => r.Fill is SolidColorBrush brush && brush.Color == ErrorBarColor && r.ActualHeight > 0)
            .ToList();
        Assert.NotEmpty(errorBars);
        Assert.All(errorBars, r => Assert.True(r.ActualWidth > 0, "error bar has zero width"));

        var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        SaveIfRequested(bitmap);

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

    /// <summary>Set LOGVIEWER_RENDER_DUMP to a .png path to look at what the test rendered.</summary>
    private static void SaveIfRequested(BitmapSource bitmap)
    {
        if (Environment.GetEnvironmentVariable("LOGVIEWER_RENDER_DUMP") is not { Length: > 0 } path)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Loads App.xaml's resource section into the test Application without instantiating <c>App</c>
    /// (whose startup reads the real settings). The view's StaticResources resolve during InitializeComponent,
    /// before it has a parent, so they have to live in Application.Resources.</summary>
    private static void LoadAppResources()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(System.IO.Path.Combine(dir, "LogViewer.slnx")))
        {
            dir = System.IO.Path.GetDirectoryName(dir)!;
        }

        var xaml = File.ReadAllText(System.IO.Path.Combine(dir, "src", "LogViewer.App", "App.xaml"));
        const string OpenTag = "<Application.Resources>";
        var start = xaml.IndexOf(OpenTag, StringComparison.Ordinal) + OpenTag.Length;
        var end = xaml.IndexOf("</Application.Resources>", StringComparison.Ordinal);
        var dictionary = $$"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:converters="clr-namespace:LogViewer.App.Converters;assembly=LogViewer.App"
                                xmlns:controls="clr-namespace:LogViewer.App.Controls;assembly=LogViewer.App"
                                xmlns:vm="clr-namespace:LogViewer.App.ViewModels;assembly=LogViewer.App"
                                xmlns:views="clr-namespace:LogViewer.App.Views.Documents;assembly=LogViewer.App">
            {{xaml[start..end]}}
            </ResourceDictionary>
            """;
        Application.Current!.Resources = (ResourceDictionary)XamlReader.Parse(dictionary);
    }

    /// <summary>The parts of <c>ThemeService.Apply(BuiltInThemes.Dark)</c> this view needs: Fluent Dark chrome and the
    /// app's own <c>Theme.*</c> brushes. (Apply itself also recolors static brushes other tests' threads already own.)</summary>
    private static void ApplyDarkTheme()
    {
#pragma warning disable WPF0001 // ThemeMode is experimental in this SDK.
        Application.Current!.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        foreach (var key in new[] { ThemeColorKeys.BorderColor, ThemeColorKeys.LogBackground, ThemeColorKeys.LogForeground })
        {
            var color = (Color)ColorConverter.ConvertFromString(BuiltInThemes.Dark.GetColor(key));
            Application.Current.Resources["Theme." + key] = new SolidColorBrush(color);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> on an STA thread with a WPF <see cref="Application"/> that is torn down again
    /// afterwards. View-models marshal through <c>Application.Current?.Dispatcher</c>, so a leftover instance whose
    /// thread has exited would make every later test in this process hang; the rest of the suite expects none.
    /// </summary>
    private static void RunStaWithTemporaryApplication(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                Assert.Null(Application.Current);
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                typeof(Application)
                    .GetField("_appInstance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .SetValue(null, null);
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        Assert.Null(Application.Current);
    }

    public void Dispose() => _tempDir.Dispose();
}
