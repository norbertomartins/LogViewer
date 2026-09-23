using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;
using LogViewer.Core.Analysis;

namespace LogViewer.App.Views.Dialogs;

public partial class ExceptionGroupsView : Window
{
    public ExceptionGroupsView()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as ExceptionGroupsViewModel)?.Dispose();
    }

    private void OnGroupDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem { DataContext: ExceptionGroup group } && DataContext is ExceptionGroupsViewModel vm)
        {
            vm.JumpToLineCommand.Execute(group.LastLineNumber);
        }
    }

    private void OnOccurrenceDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is ListBoxItem { DataContext: long lineNumber } && DataContext is ExceptionGroupsViewModel vm)
        {
            vm.JumpToLineCommand.Execute(lineNumber);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
