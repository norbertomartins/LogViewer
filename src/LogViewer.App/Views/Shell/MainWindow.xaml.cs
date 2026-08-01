using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;

namespace LogViewer.App.Views.Shell;

public partial class MainWindow : Window
{
    private readonly List<KeyBinding> _externalToolBindings = [];

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Host.AttachDockingManager(DockManager);
            ApplyWindowLayout(viewModel.WindowLayout);
            viewModel.ExternalToolsChanged += SyncExternalToolShortcuts;
            SyncExternalToolShortcuts();
        }

        DockManager.DocumentClosed += OnDocumentClosed;
        DockManager.ActiveContentChanged += OnActiveContentChanged;
        TrayIcon.Icon = System.Drawing.SystemIcons.Application;

        Dispatcher.BeginInvoke(new Action(SyncActiveDocumentTabBackground), DispatcherPriority.Loaded);
    }

    private void OnActiveContentChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(new Action(SyncActiveDocumentTabBackground), DispatcherPriority.Loaded);

    /// <summary>
    /// AvalonDock's document tab strip generates each header as a plain WPF TabItem and swaps in its own
    /// code-built Style (with a ControlTemplate trigger hardcoding Background to White) the first time a
    /// tab becomes selected — that swap is a local Style assignment, so no XAML-level style override
    /// (however it's scoped) can ever intercept it. Worse, once a tab has been active at least once,
    /// AvalonDock keeps that swapped style for its unselected look too, which turns out to be a near-white
    /// gray, not the pleasant dark gradient a never-yet-selected tab shows — so ClearValue alone isn't
    /// reliable either. Forcing Background as a resource reference on every tab, every time, is the only
    /// point that reliably wins regardless of AvalonDock's internal state: a local value beats any
    /// style/trigger, and SetResourceReference keeps it theme-reactive since ThemeService mutates the
    /// brush instances in place rather than replacing them.
    /// </summary>
    private void SyncActiveDocumentTabBackground()
    {
        foreach (var tabItem in FindVisualDescendants<TabItem>(DockManager))
        {
            tabItem.SetResourceReference(Control.BackgroundProperty, tabItem.IsSelected ? "Theme.LogBackground" : "Theme.WorkspaceBackground");
            tabItem.SetResourceReference(Control.ForegroundProperty, "Theme.LogForeground");
        }
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);

        string? layoutXml = null;
        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            using var writer = new StringWriter();
            serializer.Serialize(writer);
            layoutXml = writer.ToString();
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            // Layout couldn't be serialized (e.g. mid-teardown) — next launch just falls back to the default layout.
        }

        viewModel.CaptureWindowLayout(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized, layoutXml);
    }

    private void ApplyWindowLayout(WindowLayoutSettings layout)
    {
        if (layout.WindowLeft is { } left && layout.WindowTop is { } top && layout.WindowWidth is { } width && layout.WindowHeight is { } height)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

            // A saved position on a monitor that's since been disconnected would otherwise strand the window off-screen.
            if (left >= virtualLeft && top >= virtualTop && left + width <= virtualRight && top + height <= virtualBottom)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }
        }

        if (layout.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }

        if (string.IsNullOrEmpty(layout.DockingLayoutXml) || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        try
        {
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.LayoutSerializationCallback += (_, args) =>
            {
                var match = viewModel.Documents.FirstOrDefault(d => d.SourcePath == args.Model.ContentId);
                args.Content = match;
                args.Cancel = match is null;
            };

            using var reader = new StringReader(layout.DockingLayoutXml);
            serializer.Deserialize(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            // Corrupt/incompatible saved layout — fall back to the default LayoutRoot already declared in XAML.
        }
    }

    private void SyncExternalToolShortcuts()
    {
        foreach (var binding in _externalToolBindings)
        {
            InputBindings.Remove(binding);
        }

        _externalToolBindings.Clear();

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        foreach (var document in viewModel.Documents)
        {
            foreach (var tool in document.ExternalTools)
            {
                if (string.IsNullOrWhiteSpace(tool.ShortcutGesture))
                {
                    continue;
                }

                if (new KeyGestureConverter().ConvertFromString(tool.ShortcutGesture) is not KeyGesture gesture)
                {
                    continue;
                }

                var binding = new KeyBinding(document.RunExternalToolCommand, gesture) { CommandParameter = tool };
                InputBindings.Add(binding);
                _externalToolBindings.Add(binding);
            }

            break; // Tool set is global (per-document ExternalTools mirrors the same AppSettings list), so one pass suffices.
        }
    }

    private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
    {
        Hide();
        TrayIcon.Visibility = Visibility.Visible;
    }

    private void OnTrayShowClick(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        TrayIcon.Visibility = Visibility.Collapsed;
    }

    private static void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        if (e.Document.Content is TailDocumentViewModel document)
        {
            document.Dispose();
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                viewModel.OpenPath(path);
            }
            else if (Directory.Exists(path))
            {
                viewModel.PromptOpenDirectoryWatch(path);
            }
        }
    }
}
