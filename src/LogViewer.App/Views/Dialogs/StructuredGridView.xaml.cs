using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using LogViewer.App.Localization;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

/// <summary>The grid's columns depend on which properties the user ticks, so they're built here rather than in XAML;
/// each column's <see cref="DataGridColumn.SortMemberPath"/> carries its <see cref="StructuredGridViewModel"/> column key.
/// Sorting is done by the view-model (numbers numerically, missing values last), not by the DataGrid.</summary>
public partial class StructuredGridView : Window
{
    private StructuredGridViewModel? _viewModel;
    private StructuredGridRow? _menuRow;
    private string? _menuColumn;
    private bool _menuFromMouse;

    public StructuredGridView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += (_, _) => _viewModel?.Dispose();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged -= RebuildColumns;
        }

        _viewModel = e.NewValue as StructuredGridViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ColumnsChanged += RebuildColumns;
        }

        RebuildColumns();
    }

    private void RebuildColumns()
    {
        EventGrid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        AddColumn(Loc.Get("Grid_Col_Line"), StructuredGridViewModel.LineColumn, new Binding(nameof(StructuredGridRow.LineNumber)), 70);
        AddColumn(Loc.Get("Grid_Col_Time"), StructuredGridViewModel.TimeColumn,
            new Binding(nameof(StructuredGridRow.Timestamp)) { Converter = (IValueConverter)FindResource("TimestampToLocalTimeConverter") }, 170);
        AddColumn(Loc.Get("Grid_Col_Level"), StructuredGridViewModel.LevelColumn, new Binding(nameof(StructuredGridRow.Level)), 105);
        for (var i = 0; i < _viewModel.VisiblePropertyColumns.Count; i++)
        {
            var name = _viewModel.VisiblePropertyColumns[i];
            AddColumn(name, name, new Binding($"{nameof(StructuredGridRow.Values)}[{i}]"), 140);
        }

        AddColumn(Loc.Get("Grid_Col_Message"), StructuredGridViewModel.MessageColumn, new Binding(nameof(StructuredGridRow.Message)), 600);
        ShowSortDirection();
    }

    private void AddColumn(string header, string key, Binding binding, double width)
    {
        binding.Mode = BindingMode.OneWay;
        EventGrid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = binding,
            SortMemberPath = key,
            Width = width,
        });
    }

    private void ShowSortDirection()
    {
        foreach (var column in EventGrid.Columns)
        {
            column.SortDirection = column.SortMemberPath == _viewModel?.SortColumn
                ? _viewModel.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        }
    }

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        _viewModel?.SortBy(e.Column.SortMemberPath);
        ShowSortDirection();
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) is { Item: StructuredGridRow row })
        {
            _viewModel?.JumpToLineCommand.Execute(row);
        }
    }

    /// <summary>Remembers the right-clicked cell (and selects its row) for the context menu.</summary>
    private void OnGridPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        _menuRow = cell?.DataContext as StructuredGridRow;
        _menuColumn = cell?.Column?.SortMemberPath;
        _menuFromMouse = true;
        if (_menuRow is not null && _viewModel is not null)
        {
            _viewModel.SelectedRow = _menuRow;
        }
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        // The cell is kept until the next opening: menu item clicks are dispatched after the menu has closed.
        // Opened from the keyboard (or the right-click missed a cell): use the current cell, or the selected row's level.
        if (!_menuFromMouse || _menuRow is null)
        {
            _menuRow = EventGrid.CurrentCell.Item as StructuredGridRow ?? _viewModel?.SelectedRow;
            _menuColumn = EventGrid.CurrentCell.Column?.SortMemberPath ?? StructuredGridViewModel.LevelColumn;
        }

        var filterable = _menuRow is not null && _menuColumn is not (null or StructuredGridViewModel.LineColumn or StructuredGridViewModel.TimeColumn);
        FilterByValueItem.IsEnabled = filterable;
        ExcludeValueItem.IsEnabled = filterable;
        if (filterable)
        {
            var value = StructuredGridViewModel.CellValue(_menuRow!, _menuColumn!) ?? "∅";
            FilterByValueItem.Header = Loc.Format("Grid_Ctx_FilterByValueFmt", _menuColumn, Shorten(value));
            ExcludeValueItem.Header = Loc.Format("Grid_Ctx_ExcludeValueFmt", _menuColumn, Shorten(value));
        }
        else
        {
            FilterByValueItem.Header = Loc.Get("Grid_Ctx_FilterByValue");
            ExcludeValueItem.Header = Loc.Get("Grid_Ctx_ExcludeValue");
        }

        _menuFromMouse = false;
    }

    private static string Shorten(string value) => value.Length > 40 ? value[..40] + "…" : value;

    private void OnFilterByValue(object sender, RoutedEventArgs e) => AddMenuFilter(exclude: false);

    private void OnExcludeValue(object sender, RoutedEventArgs e) => AddMenuFilter(exclude: true);

    private void AddMenuFilter(bool exclude)
    {
        if (_menuRow is not null && _menuColumn is not null)
        {
            _viewModel?.AddFilter(_menuColumn, StructuredGridViewModel.CellValue(_menuRow, _menuColumn), exclude);
        }
    }

    private void OnGoToLine(object sender, RoutedEventArgs e) => _viewModel?.JumpToLineCommand.Execute(_menuRow ?? _viewModel.SelectedRow);

    private void OnCopyValue(object sender, RoutedEventArgs e)
    {
        if (_menuRow is null || _menuColumn is null || StructuredGridViewModel.CellValue(_menuRow, _menuColumn) is not { } value)
        {
            return;
        }

        try
        {
            Clipboard.SetText(value);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard busy (another app holds it); the user can retry.
        }
    }

    private static T? FindAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
