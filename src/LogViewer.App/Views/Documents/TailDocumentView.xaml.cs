using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LogViewer.App.Models;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Documents;

public partial class TailDocumentView : UserControl
{
    private TailDocumentViewModel? _viewModel;
    private ScrollViewer? _lineListScrollViewer;

    public TailDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested -= OnScrollToLineRequested;
        }

        _viewModel = e.NewValue as TailDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
        }
    }

    private void OnScrollToEndRequested()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (LineListView.Items.Count > 0)
            {
                LineListView.ScrollIntoView(LineListView.Items[^1]);
            }

            ResetHorizontalScrollAfterLayout();
        });
    }

    private void OnScrollToLineRequested(LogLineViewModel line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LineListView.ScrollIntoView(line);
            ResetHorizontalScrollAfterLayout();
        });
    }

    /// <summary>ScrollIntoView on a long (unwrapped) line can drag the horizontal scroll offset away from
    /// zero, hiding the start of every line. Line starts matter far more than the tail of one long line, so
    /// pin it back to zero once the ScrollIntoView layout pass has actually run.</summary>
    private void ResetHorizontalScrollAfterLayout()
    {
        Dispatcher.BeginInvoke(() => GetLineListScrollViewer()?.ScrollToHorizontalOffset(0), DispatcherPriority.ContextIdle);
    }

    private ScrollViewer? GetLineListScrollViewer()
    {
        if (_lineListScrollViewer is null && LineListView.IsLoaded)
        {
            _lineListScrollViewer = FindDescendant<ScrollViewer>(LineListView);
        }

        return _lineListScrollViewer;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
