using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LogViewer.App.Tests.TestUtilities;
using Xunit.Abstractions;

namespace LogViewer.App.Tests.Views;

/// <summary>Measures the real (Fluent-themed) height of the main menu bar, document toolbar and collapsed detail
/// header, so "make it shorter" claims are backed by numbers rather than by reading XAML.</summary>
[Collection(WpfApplicationCollection.Name)]
public sealed class ChromeHeightMeasurementTests(ITestOutputHelper output)
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static double Measure(FrameworkElement element, double width = 1200)
    {
        element.Measure(new Size(width, 400));
        element.Arrange(new Rect(0, 0, width, element.DesiredSize.Height));
        element.UpdateLayout();
        return element.DesiredSize.Height;
    }

    /// <summary>Reads the Menu element out of MainWindow.xaml so the test follows the real markup.</summary>
    private static string MainMenuXaml()
    {
        var xaml = File.ReadAllText(Path.Combine(OffscreenWpf.RepoRootPath(), "src", "LogViewer.App", "Views", "Shell", "MainWindow.xaml"));
        var start = xaml.IndexOf("<Menu ", StringComparison.Ordinal);
        var openEnd = xaml.IndexOf('>', start);
        var openTag = xaml[start..(openEnd + 1)].Replace("DockPanel.Dock=\"Top\"", "");
        // Keep only the header-level look: top-level items are what determine the bar's height.
        var items = System.Text.RegularExpressions.Regex.Matches(xaml[start..xaml.IndexOf("</Menu>", StringComparison.Ordinal)],
            @"<MenuItem Header=""\{loc:Loc Menu_(File|WindowMode|View|Session|Tools)\}""([^>]*)>");
        var body = string.Concat(items.Select(m => $"<MenuItem Header=\"{m.Groups[1].Value}\"{m.Groups[2].Value.Replace("Padding", "Padding")}><MenuItem Header=\"child\" /></MenuItem>"));
        return openTag.Replace("<Menu ", $"<Menu {Ns} ") + body + "</Menu>";
    }

    /// <summary>The main window's bottom StatusBar (performance readout), straight out of MainWindow.xaml with bindings
    /// replaced by sample text so its line height is measured the way it renders with content.</summary>
    private static string MainStatusBarXaml()
    {
        var xaml = File.ReadAllText(Path.Combine(OffscreenWpf.RepoRootPath(), "src", "LogViewer.App", "Views", "Shell", "MainWindow.xaml"));
        var start = xaml.IndexOf("<StatusBar DockPanel.Dock=\"Bottom\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("</StatusBar>", start, StringComparison.Ordinal) + "</StatusBar>".Length;
        var bar = xaml[start..end]
            .Replace("<StatusBar ", $"<StatusBar {Ns} ")
            .Replace("Text=\"{Binding StatusMessage}\"", "Text=\"Ready\"")
            .Replace("Text=\"{Binding PerformanceStatus}\"", "Text=\"1234 lines/s | RAM 512 MB\"");
        return System.Text.RegularExpressions.Regex.Replace(bar, @"\s+[\w:.]+=""\{(loc:|Binding)[^""]*""", "");
    }

    /// <summary>The real document toolbar, straight out of TailDocumentView.xaml: popups and item-container styles
    /// (pure binding plumbing) are cut out, then every loc/Binding attribute is stripped so the markup parses without a
    /// view-model. Visibility bindings go too, so every conditionally-visible button is measured as well.</summary>
    private static string ToolBarXaml(bool compactMenus = true)
    {
        var xaml = File.ReadAllText(Path.Combine(OffscreenWpf.RepoRootPath(), "src", "LogViewer.App", "Views", "Documents", "TailDocumentView.xaml"));
        var start = xaml.IndexOf("<ToolBar ", StringComparison.Ordinal);
        var end = xaml.IndexOf("</ToolBar>", start, StringComparison.Ordinal) + "</ToolBar>".Length;
        var toolBar = xaml[start..end]
            .Replace("Background=\"{DynamicResource Theme.LogBackground}\"", "").Replace("Foreground=\"{DynamicResource Theme.LogForeground}\"", "")
            .Replace("Grid.Row=\"0\"", "").Replace("<ToolBar ", $"<ToolBar {Ns} ")
            .Replace("Content=\"{Binding NewPatternBadge}\"", "Content=\"N\"").Replace("Content=\"{Binding ResumeFollowBanner}\"", "Content=\"Resume\"");
        toolBar = System.Text.RegularExpressions.Regex.Replace(toolBar, @"<Popup\b.*?</Popup>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        toolBar = System.Text.RegularExpressions.Regex.Replace(toolBar, @"<MenuItem\.ItemContainerStyle>.*?</MenuItem\.ItemContainerStyle>", "", System.Text.RegularExpressions.RegexOptions.Singleline);
        toolBar = System.Text.RegularExpressions.Regex.Replace(toolBar, @"\s+[\w:.]+=""\{(loc:|Binding)[^""]*""", "");
        return compactMenus ? toolBar : toolBar.Replace("Style=\"{StaticResource CompactTopLevelMenuItemStyle}\"", "");
    }

    /// <summary>Vertical ink extent (top, bottom, centre) of what is actually painted inside <paramref name="bounds"/>
    /// of the rendered toolbar; layout centres can agree while glyphs from different fonts sit at different heights.</summary>
    private static (int Top, int Bottom, double Center)? InkExtent(byte[] pixels, int stride, int height, Rect bounds)
    {
        int top = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = (int)Math.Floor(bounds.Left); x < (int)Math.Ceiling(bounds.Right); x++)
            {
                var i = (y * stride) + (x * 4);
                // Background is mid-grey (128) so both light and dark glyphs stand out from it.
                if (Math.Abs(pixels[i] - 128) > 60 || Math.Abs(pixels[i + 1] - 128) > 60 || Math.Abs(pixels[i + 2] - 128) > 60)
                {
                    top = top < 0 ? y : top;
                    bottom = y;
                    break;
                }
            }
        }

        return top < 0 ? null : (top, bottom, (top + bottom + 1) / 2.0);
    }

    /// <summary>The collapsed detail panel is its header strip only: measures the real header markup (bindings
    /// stripped) inside the same Border/Grid, and checks that the header, not stray min-sizes, sets the height.</summary>
    [Fact]
    public void CollapsedDetailPanel_IsAsTallAsItsHeaderStrip() => OffscreenWpf.RunStaWithTemporaryApplication(() =>
    {
        OffscreenWpf.LoadAppResources();
        OffscreenWpf.ApplyDarkTheme();

        var xaml = File.ReadAllText(Path.Combine(OffscreenWpf.RepoRootPath(), "src", "LogViewer.App", "Views", "Documents", "TailDocumentView.xaml"));
        var start = xaml.IndexOf("<DockPanel Grid.Row=\"0\" LastChildFill=\"False\">", StringComparison.Ordinal);
        var end = xaml.IndexOf("</DockPanel>", start, StringComparison.Ordinal) + "</DockPanel>".Length;
        var header = System.Text.RegularExpressions.Regex.Replace(xaml[start..end], @"\s+[\w:.]+=""\{(loc|Binding)[^""]*""", "");
        header = header.Replace("<DockPanel ", $"<DockPanel {Ns} ").Replace("<TextBlock ", "<TextBlock Text=\"Properties\" ").Replace("<CheckBox ", "<CheckBox Content=\"Hover to open\" ");
        var styleStart = xaml.IndexOf("<Style x:Key=\"DetailCollapseButtonStyle\"", StringComparison.Ordinal);
        var buttonStyle = xaml[styleStart..(xaml.IndexOf("</Style>", styleStart, StringComparison.Ordinal) + "</Style>".Length)];
        var grid = (Grid)XamlReader.Parse($"<Grid {Ns}><Grid.RowDefinitions><RowDefinition Height=\"Auto\" /><RowDefinition Height=\"*\" /></Grid.RowDefinitions><Grid.Resources>{buttonStyle}</Grid.Resources>{header.Replace(" " + Ns, "")}</Grid>");
        var border = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Child = grid };
        var total = Measure(border);
        var dock = (DockPanel)grid.Children[0];
        output.WriteLine($"DETAIL collapsed border={total} header={dock.DesiredSize.Height}");
        foreach (var child in dock.Children.OfType<FrameworkElement>())
        {
            output.WriteLine($"  {child.GetType().Name} h={child.DesiredSize.Height} minH={child.MinHeight}");
        }

        Assert.Equal(dock.DesiredSize.Height + 1, total, 1);
        Assert.True(dock.DesiredSize.Height <= 24, $"header strip is {dock.DesiredSize.Height}px tall");
    });

    [Fact]
    public void MeasureMainStatusBar() => OffscreenWpf.RunStaWithTemporaryApplication(() =>
    {
        OffscreenWpf.LoadAppResources();
        OffscreenWpf.ApplyDarkTheme();

        var statusBar = (System.Windows.Controls.Primitives.StatusBar)XamlReader.Parse(MainStatusBarXaml());
        var height = Measure(statusBar);
        output.WriteLine($"MAIN STATUSBAR height={height}");
        Assert.True(height <= 22, $"status bar is {height} px tall");
    });

    [Fact]
    public void MeasureMenuAndToolBar() => OffscreenWpf.RunStaWithTemporaryApplication(() =>
    {
        OffscreenWpf.LoadAppResources();
        OffscreenWpf.ApplyDarkTheme();

        var menu = (Menu)XamlReader.Parse(MainMenuXaml());
        var menuHeight = Measure(menu);
        output.WriteLine($"MENU height={menuHeight}");
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            output.WriteLine($"  item {item.Header} h={item.DesiredSize.Height} pad={item.Padding} minH={item.MinHeight}");
        }

        var legacyToolBar = (ToolBar)XamlReader.Parse(ToolBarXaml(compactMenus: false));
        output.WriteLine($"TOOLBAR (Fluent nested menu, before) height={Measure(legacyToolBar, 3000)}");
        var toolBar = (ToolBar)XamlReader.Parse(ToolBarXaml());
        toolBar.Background = new SolidColorBrush(Color.FromRgb(128, 128, 128));
        // The "resume follow" banner is only shown while there are unseen changes: measure with and without it.
        var resume = toolBar.Items.OfType<Button>().Last();
        var withResumeHeight = Measure(toolBar, 3000);
        output.WriteLine($"TOOLBAR with resume banner height={withResumeHeight}");
        resume.Visibility = Visibility.Collapsed;
        var toolBarHeight = Measure(toolBar, 3000);
        output.WriteLine($"TOOLBAR height={toolBarHeight}");

        var width = 3000;
        toolBar.Measure(new Size(width, 400));
        toolBar.Arrange(new Rect(0, 0, width, toolBar.DesiredSize.Height));
        toolBar.UpdateLayout();
        var height = (int)Math.Ceiling(toolBar.ActualHeight);
        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(toolBar);
        var stride = width * 4;
        var pixels = new byte[stride * height];
        target.CopyPixels(pixels, stride, 0);
        OffscreenWpf.SaveIfRequested(target, "toolbar");

        var iconCenters = new List<double>();
        foreach (var child in toolBar.Items.OfType<FrameworkElement>().Where(c => c is not Separator))
        {
            var bounds = child.TransformToAncestor(toolBar).TransformBounds(new Rect(0, 0, child.ActualWidth, child.ActualHeight));
            var ink = InkExtent(pixels, stride, height, bounds);
            var label = child switch { ContentControl cc => cc.Content, HeaderedItemsControl => "menu", _ => null } ?? (child as TextBlock)?.Text ?? (child as Menu)?.Items.OfType<MenuItem>().FirstOrDefault()?.Header;
            output.WriteLine($"  {child.GetType().Name} '{label}' h={child.ActualHeight:0.0} layoutCentre={bounds.Top + (bounds.Height / 2):0.0} ink={(ink is { } i ? $"{i.Top}-{i.Bottom} centre={i.Center:0.0}" : "none")} fs={System.Windows.Documents.TextElement.GetFontSize(child)}");
            if (child is ButtonBase or TextBlock or Menu && ink is { } glyph)
            {
                iconCenters.Add(glyph.Center);
            }
        }

        output.WriteLine($"ICON ink centres min={iconCenters.Min():0.0} max={iconCenters.Max():0.0}");
        Assert.True(menuHeight <= 30, $"menu bar is {menuHeight}px tall");
        Assert.True(toolBarHeight <= 26, $"toolbar is {toolBarHeight}px tall");
        Assert.True(withResumeHeight <= 26, $"toolbar with the resume banner is {withResumeHeight}px tall");
        Assert.True(iconCenters.Max() - iconCenters.Min() <= 1, $"icon vertical centres differ by {iconCenters.Max() - iconCenters.Min()}px");
    });
}
