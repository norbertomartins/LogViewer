using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LogViewer.App.Controls;
using LogViewer.App.Localization;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;

namespace LogViewer.App.Views.Documents;

public partial class TailDocumentView : UserControl
{
    private static readonly Brush ErrorMarkerBrush = Frozen(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly Brush WarningMarkerBrush = Frozen(Color.FromRgb(0xFF, 0xB3, 0x00));
    private static readonly Brush BookmarkMarkerBrush = Frozen(Color.FromRgb(0x1E, 0x88, 0xE5));
    private static readonly Brush NewPatternMarkerBrush = Frozen(Color.FromRgb(0xD8, 0x1B, 0x60));
    private static readonly int ErrorRank = LogLevelSeverity.Rank("Error")!.Value;
    private static readonly int WarningRank = LogLevelSeverity.Rank("Warning")!.Value;

    private readonly DispatcherTimer _markerRefreshTimer;
    private TailDocumentViewModel? _viewModel;
    private ScrollViewer? _lineListScrollViewer;

    public TailDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Tail flushes arrive every ~100ms; redrawing the strip at most every 400ms keeps it cheap under load.
        _markerRefreshTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _markerRefreshTimer.Tick += (_, _) =>
        {
            _markerRefreshTimer.Stop();
            RefreshScrollMarkers();
        };
        MarkerStrip.PositionClicked += OnMarkerStripClicked;
        MarkerStrip.SizeChanged += (_, _) => ScheduleMarkerRefresh();
        ((System.Collections.Specialized.INotifyCollectionChanged)LineListView.Items).CollectionChanged += (_, _) => ScheduleMarkerRefresh();
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void ScheduleMarkerRefresh()
    {
        if (!_markerRefreshTimer.IsEnabled)
        {
            _markerRefreshTimer.Start();
        }
    }

    /// <summary>Rebuilds the overview strip from the currently visible (post-filter) lines: bookmarks first, then
    /// new error patterns, errors, warnings and highlight matches — the strip keeps the highest-priority mark per pixel row.</summary>
    private void RefreshScrollMarkers()
    {
        var items = LineListView.Items;
        var count = items.Count;
        var markers = new List<ScrollMarker>();
        if (count > 0)
        {
            var scale = count > 1 ? 1.0 / (count - 1) : 0;
            var index = 0;
            foreach (var item in items)
            {
                if (item is LogLineViewModel line)
                {
                    var position = index * scale;
                    if (line.IsBookmarked)
                    {
                        markers.Add(new ScrollMarker(position, BookmarkMarkerBrush, 0));
                    }
                    else if (line.IsNewPattern)
                    {
                        markers.Add(new ScrollMarker(position, NewPatternMarkerBrush, 1));
                    }
                    else if (line.SeverityRank is { } rank && rank >= ErrorRank)
                    {
                        markers.Add(new ScrollMarker(position, ErrorMarkerBrush, 2));
                    }
                    else if (line.SeverityRank is { } warnRank && warnRank >= WarningRank)
                    {
                        markers.Add(new ScrollMarker(position, WarningMarkerBrush, 3));
                    }
                    else if (line.HighlightMarkerBrush is { } highlight)
                    {
                        markers.Add(new ScrollMarker(position, highlight, 4));
                    }
                }

                index++;
            }
        }

        MarkerStrip.SetMarkers(markers);
    }

    /// <summary>Clicking the strip scrolls to the proportional line. A user gesture, so it isn't marked as a
    /// programmatic scroll — the scroll-changed handler pauses follow exactly as a manual scroll would.</summary>
    private void OnMarkerStripClicked(double fraction)
    {
        var count = LineListView.Items.Count;
        if (count == 0 || !LineListView.IsLoaded)
        {
            return;
        }

        var target = LineListView.Items[(int)Math.Round(fraction * (count - 1))];
        try
        {
            LineListView.ScrollIntoView(target);
            LineListView.SelectedItem = target;
            ResetHorizontalScrollAfterLayout();
        }
        catch (InvalidOperationException)
        {
            // Mid-reparent during a window-mode switch; the user can click again.
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
            _viewModel.ScrollToStartRequested -= OnScrollToStartRequested;
            _viewModel.ScrollToLineRequested -= OnScrollToLineRequested;
            _viewModel.FilterChanged -= OnFilterChanged;
            _viewModel.ExportRequested -= OnExportRequested;
            _viewModel.ScrollMarkersInvalidated -= ScheduleMarkerRefresh;
        }

        _viewModel = e.NewValue as TailDocumentViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
            _viewModel.ScrollToStartRequested += OnScrollToStartRequested;
            _viewModel.ScrollToLineRequested += OnScrollToLineRequested;
            _viewModel.FilterChanged += OnFilterChanged;
            _viewModel.ExportRequested += OnExportRequested;
            _viewModel.ScrollMarkersInvalidated += ScheduleMarkerRefresh;
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
        var hideBeforeLineNumber = _viewModel?.HideBeforeLineNumber;
        var hasTimeFilter = _viewModel?.IsTimeFilterActive ?? false;
        var hasCorrelationFilter = _viewModel?.IsCorrelationFilterActive ?? false;

        if (value is null && minLevelRank is null && !hasTextFilter && hideBeforeLineNumber is null && !hasTimeFilter && !hasCorrelationFilter)
        {
            view.Filter = null;
            return;
        }

        view.Filter = item => item is LogLineViewModel line
            && (value is null || string.Equals(StructuredFieldResolver.Resolve(line.Structured, field!), value, StringComparison.Ordinal))
            && (minLevelRank is null || ((LogLevelSeverity.Rank(line.Structured?.Level) ?? LogLevelNormalizer.GuessSeverityFromLine(line.Text)) is { } rank && rank >= minLevelRank))
            && (!hasTextFilter || _viewModel!.PassesTextFilter(line.Text))
            && (hideBeforeLineNumber is null || line.LineNumber >= hideBeforeLineNumber)
            && (!hasTimeFilter || _viewModel!.PassesTimeFilter(line))
            && (!hasCorrelationFilter || _viewModel!.PassesCorrelationFilter(line));
    }

    /// <summary>Exports or copies the effective line set — the user's <see cref="ListView.SelectedItems"/>
    /// when non-empty, otherwise every currently visible (post-filter) line. The filtered/selected set
    /// lives in this view's <see cref="ICollectionView"/>/<c>ListView</c>, so this is a view concern, not
    /// something the view-model can resolve itself.</summary>
    private void OnExportRequested(ExportTarget target)
    {
        if (_viewModel is null)
        {
            return;
        }

        var lines = (LineListView.SelectedItems.Count > 0 ? LineListView.SelectedItems : LineListView.Items)
            .OfType<LogLineViewModel>().ToList();

        switch (target)
        {
            case ExportTarget.File:
                ExportToFile(lines);
                break;
            case ExportTarget.Clipboard:
                CopyToClipboard(lines);
                break;
            case ExportTarget.ClipboardJson:
                CopyToClipboardAsJson(lines);
                break;
            case ExportTarget.ClipboardFormatted:
                CopyToClipboardFormatted(lines);
                break;
        }
    }

    private void ExportToFile(IReadOnlyList<LogLineViewModel> lines)
    {
        var suggested = $"{_viewModel!.Title}-{DateTime.Now:yyyyMMdd-HHmmss}.log";
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
            System.IO.File.WriteAllText(dialog.FileName, LogLineExportFormatter.ToPlainText(lines));
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_Done", lines.Count, System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    private void CopyToClipboard(IReadOnlyList<LogLineViewModel> lines)
    {
        try
        {
            Clipboard.SetText(LogLineExportFormatter.ToPlainText(lines));
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_CopiedDone", lines.Count);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    private void CopyToClipboardAsJson(IReadOnlyList<LogLineViewModel> lines)
    {
        try
        {
            Clipboard.SetText(LogLineExportFormatter.ToJson(lines));
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_CopiedJsonDone", lines.Count);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    private void CopyToClipboardFormatted(IReadOnlyList<LogLineViewModel> lines)
    {
        try
        {
            Clipboard.SetText(LogLineExportFormatter.ToFormattedText(lines));
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_CopiedFormattedDone", lines.Count);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            _viewModel!.StatusMessage = Loc.Format("Vm_Export_Failed", ex.Message);
        }
    }

    /// <summary>Selects the right-clicked row before its context menu opens, unless it's already part of
    /// the current multi-selection — see the style comment in the XAML for why this matters for the
    /// Copy/Copy JSON/Copy Formatted commands.</summary>
    private void OnLineItemPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { IsSelected: false } item)
        {
            LineListView.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    /// <summary>Fills the row's "Filter by correlation ID" submenu with the ids found on that line. Built here rather
    /// than via ItemsSource bindings because a submenu's items live in a nested popup, where bindings back to the
    /// ContextMenu's PlacementTarget (the only route to the document view-model) don't resolve reliably.</summary>
    private void OnLineItemContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListViewItem { DataContext: LogLineViewModel line, ContextMenu: { } menu } || _viewModel is null)
        {
            return;
        }

        var correlationMenu = menu.Items.OfType<MenuItem>().FirstOrDefault(m => Equals(m.Tag, "CorrelationMenu"));
        if (correlationMenu is null)
        {
            return;
        }

        correlationMenu.Items.Clear();
        foreach (var id in line.CorrelationIds)
        {
            correlationMenu.Items.Add(new MenuItem
            {
                Header = $"{id.Name} = {id.Value}",
                Command = _viewModel.FilterByCorrelationCommand,
                CommandParameter = id,
            });
        }

        correlationMenu.IsEnabled = correlationMenu.Items.Count > 0;
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

    private void OnScrollToStartRequested()
    {
        Dispatcher.BeginInvoke(() => SafeScrollIntoView(() =>
        {
            if (LineListView.Items.Count > 0)
            {
                LineListView.ScrollIntoView(LineListView.Items[0]);
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
