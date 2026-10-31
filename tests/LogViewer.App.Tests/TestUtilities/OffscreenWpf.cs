using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LogViewer.Core.Theming;

namespace LogViewer.App.Tests.TestUtilities;

/// <summary>Helpers for rendering real views offscreen (works while the Windows session is locked).</summary>
internal static class OffscreenWpf
{
    /// <summary>
    /// Runs <paramref name="action"/> on an STA thread with a WPF <see cref="Application"/> that is torn down again
    /// afterwards. View-models marshal through <c>Application.Current?.Dispatcher</c>, so a leftover instance whose
    /// thread has exited would make every later test in this process hang; the rest of the suite expects none.
    /// </summary>
    public static void RunStaWithTemporaryApplication(Action action)
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
                // Both statics guard "one Application per AppDomain"; clear them so the next render test can create its own.
                const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                typeof(Application).GetField("_appInstance", Flags)!.SetValue(null, null);
                typeof(Application).GetField("_appCreatedInThisAppDomain", Flags)!.SetValue(null, false);
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

    /// <summary>Loads App.xaml's resource section into the test Application without instantiating <c>App</c>
    /// (whose startup reads the real settings). Views resolve StaticResources during InitializeComponent,
    /// before they have a parent, so they have to live in Application.Resources.</summary>
    public static void LoadAppResources()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "LogViewer.App", "App.xaml"));
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

    /// <summary>The parts of <c>ThemeService.Apply(BuiltInThemes.Dark)</c> views need: Fluent Dark chrome and the
    /// app's own <c>Theme.*</c> brushes. (Apply itself also recolors static brushes other tests' threads already own.)</summary>
    public static void ApplyDarkTheme()
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

    public static IEnumerable<DependencyObject> Descendants(DependencyObject root)
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

    /// <summary>Set LOGVIEWER_RENDER_DUMP to a .png path (or, with several renders, a directory) to look at what
    /// a test rendered.</summary>
    public static void SaveIfRequested(BitmapSource bitmap, string? name = null)
    {
        if (Environment.GetEnvironmentVariable("LOGVIEWER_RENDER_DUMP") is not { Length: > 0 } path)
        {
            return;
        }

        if (name is not null)
        {
            Directory.CreateDirectory(path);
            path = Path.Combine(path, name + ".png");
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public static string RepoRootPath() => RepoRoot();

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "LogViewer.slnx")))
        {
            dir = Path.GetDirectoryName(dir)!;
        }

        return dir;
    }
}
