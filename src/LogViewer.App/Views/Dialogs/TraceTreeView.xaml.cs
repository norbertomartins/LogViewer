using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;

namespace LogViewer.App.Views.Dialogs;

public partial class TraceTreeView : Window
{
    public TraceTreeView()
    {
        InitializeComponent();
    }

    private void OnSpanDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is TreeViewItem { DataContext: TraceSpanNode node } && DataContext is TraceTreeViewModel vm)
        {
            vm.JumpToSpanCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
