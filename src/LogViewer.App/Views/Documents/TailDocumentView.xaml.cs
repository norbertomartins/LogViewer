using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LogViewer.App.Models;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;

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
            _viewModel.FilterChanged -= OnFilterChanged;
        }

        _viewModel = e.NewValue as TailDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
            _viewModel.FilterChanged += OnFilterChanged;
        }

        OnFilterChanged();
    }

    /// <summary>Applies (or clears) the active trace/span/property filter over <see cref="TailDocumentView.LineListView"/>
    /// via WPF's default <see cref="ICollectionView"/> — filtering lives here rather than in the view-model
    /// because it's a WPF collection-view concern, and it reapplies automatically on every new-lines Reset
    /// raised by <see cref="LogViewer.App.Controls.DisplayLineCollection"/> without any extra refresh code.</summary>
    private void OnFilterChanged()
    {
        var view = CollectionViewSource.GetDefaultView(LineListView.ItemsSource);
        if (view is null)
        {
            return;
        }

        var field = _viewModel?.ActiveFilterField;
        var value = _viewModel?.ActiveFilterValue;
        var minLevelRank = _viewModel?.MinLevelRank;

        if (value is null && minLevelRank is null)
        {
            view.Filter = null;
            return;
        }

        view.Filter = item => item is LogLineViewModel line
            && (value is null || string.Equals(StructuredFieldResolver.Resolve(line.Structured, field!), value, StringComparison.Ordinal))
            && (minLevelRank is null || (LogLevelSeverity.Rank(line.Structured?.Level) is { } rank && rank >= minLevelRank));
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

    /// <summary>Persists the detail panel's dragged-to height onto the view-model, which the DetailPanelRow
    /// height MultiBinding (see <c>DetailPanelRowHeightConverter</c>) then feeds right back — so this is the
    /// single point where a splitter drag becomes the new source of truth.</summary>
    private void OnDetailPanelSplitterDragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (_viewModel is not null && DetailPanelRow.ActualHeight > 0)
        {
            _viewModel.DetailPanelHeight = DetailPanelRow.ActualHeight;
        }
    }

    /// <summary>Ctrl+MouseWheel zooms the log font size instead of scrolling; a plain wheel scrolls as usual
    /// (left unhandled so the ListView's own ScrollViewer still processes it).</summary>
    private void OnLineListPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control || _viewModel is null)
        {
            return;
        }

        _viewModel.AdjustLogFontSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
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
