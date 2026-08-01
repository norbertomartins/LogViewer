using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Controls;

public partial class MdiChildWindowControl : UserControl
{
    private static int _zIndexCounter;

    public MdiChildWindowControl()
    {
        InitializeComponent();
    }

    private TailDocumentViewModel? ViewModel => DataContext as TailDocumentViewModel;

    private void OnChildMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.MdiZIndex = ++_zIndexCounter;
        }
    }

    private void OnDragThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel is not { IsMdiMaximized: false } vm)
        {
            return;
        }

        vm.MdiLeft = Math.Max(0, vm.MdiLeft + e.HorizontalChange);
        vm.MdiTop = Math.Max(0, vm.MdiTop + e.VerticalChange);
    }

    private void OnResizeThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (ViewModel is not { IsMdiMaximized: false } vm)
        {
            return;
        }

        vm.MdiWidth = Math.Max(240, vm.MdiWidth + e.HorizontalChange);
        vm.MdiHeight = Math.Max(160, vm.MdiHeight + e.VerticalChange);
    }

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        var viewport = FindAncestorScrollViewer();
        vm.ToggleMdiMaximize(viewport?.ViewportWidth ?? 800, viewport?.ViewportHeight ?? 600);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        if (System.Windows.Application.Current?.MainWindow?.DataContext is MainViewModel mainViewModel)
        {
            mainViewModel.CloseDocumentCommand.Execute(vm);
        }
    }

    private ScrollViewer? FindAncestorScrollViewer()
    {
        DependencyObject current = this;
        while (current is not null)
        {
            current = VisualTreeHelper.GetParent(current);
            if (current is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }
        }

        return null;
    }
}
