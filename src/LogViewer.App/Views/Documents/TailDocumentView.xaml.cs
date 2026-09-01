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
            _viewModel.ExportRequested -= OnExportRequested;
        }

        _viewModel = e.NewValue as TailDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
            _viewModel.FilterChanged += OnFilterChanged;
            _viewModel.ExportRequested += OnExportRequested;
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
        var hasTextFilter = _viewModel?.IsTextFilterActive ?? false;

        if (value is null && minLevelRank is null && !hasTextFilter)
        {
            view.Filter = null;
            return;
        }

        view.Filter = item => item is LogLineViewModel line
            && (value is null || string.Equals(StructuredFieldResolver.Resolve(line.Structured, field!), value, StringComparison.Ordinal))
            && (minLevelRank is null || (LogLevelSeverity.Rank(line.Structured?.Level) is { } rank && rank >= minLevelRank))
            && (!hasTextFilter || _viewModel!.PassesTextFilter(line.Text));
    }

    /// <summary>Writes the currently visible (post-filter) lines to a user-chosen text file. The filtered
    /// set lives in this view's <see cref="ICollectionView"/>, so export is a view concern.</summary>
    private void OnExportRequested()
    {
        if (_viewModel is null)
        {
            return;
        }

        var suggested = $"{_viewModel.Title}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = System.Text.RegularExpressions.Regex.Replace(suggested, "[\\\\/:*?\"<>|]", "_"),
            Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".log",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var lines = LineListView.Items.OfType<LogLineViewModel>().Select(l => l.Text);
            System.IO.File.WriteAllLines(dialog.FileName, lines);
            _viewModel.StatusMessage = $"Exported {LineListView.Items.Count} line(s) to {System.IO.Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _viewModel.StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    private void OnScrollToEndRequested()
    {
        Dispatcher.BeginInvoke(() => SafeScrollIntoView(() =>
        {
            if (LineListView.Items.Count > 0)
            {
                LineListView.ScrollIntoView(LineListView.Items[^1]);
            }
        }));
    }

    private void OnScrollToLineRequested(LogLineViewModel line)
    {
        Dispatcher.BeginInvoke(() => SafeScrollIntoView(() => LineListView.ScrollIntoView(line)));
    }

    /// <summary>Runs a <c>ScrollIntoView</c> call guarded against the transient states the ListView passes
    /// through while AvalonDock re-parents it during a window-mode switch — with a live tail (ETW, remote,
    /// process) new lines keep firing scroll requests right through the reparent, and ScrollIntoView on a
    /// ListView whose virtualizing panel is momentarily detached throws from a Dispatcher callback, which
    /// would otherwise take down the process.</summary>
    private void SafeScrollIntoView(Action scroll)
    {
        if (!IsLoaded || !LineListView.IsLoaded)
        {
            return;
        }

        try
        {
            BeginProgrammaticScroll();
            scroll();
            ResetHorizontalScrollAfterLayout();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentOutOfRangeException)
        {
            // Mid-reparent — the next flushed batch (or the user) will scroll again once layout settles.
        }
    }

    /// <summary>Marks the next scroll as ours so <see cref="OnLineListScrollChanged"/> doesn't read it as
    /// the user scrolling away and pause the follow we just performed. Cleared once layout settles.</summary>
    private void BeginProgrammaticScroll()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.IsProgrammaticScroll = true;
        Dispatcher.BeginInvoke(() => _viewModel.IsProgrammaticScroll = false, DispatcherPriority.ContextIdle);
    }

    /// <summary>Smart follow lock: scrolling up off the tail pauses follow; scrolling back to the bottom
    /// re-arms it. Content-growth scrolls (ExtentHeightChange != 0) and our own programmatic scrolls are
    /// ignored so only a real user gesture flips the state.</summary>
    private void OnLineListScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewModel is null || _viewModel.IsProgrammaticScroll || e.ExtentHeightChange != 0 || e.VerticalChange == 0)
        {
            return;
        }

        var distanceFromBottom = e.ExtentHeight - e.ViewportHeight - e.VerticalOffset;
        if (distanceFromBottom <= 2.0)
        {
            _viewModel.NotifyUserScrolledToEnd();
        }
        else
        {
            _viewModel.NotifyUserScrolledAwayFromEnd();
        }
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
