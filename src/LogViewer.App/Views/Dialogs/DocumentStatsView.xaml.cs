using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;
using LogViewer.Core.Analysis;

namespace LogViewer.App.Views.Dialogs;

public partial class DocumentStatsView : Window
{
    public DocumentStatsView()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as DocumentStatsViewModel)?.Dispose();
    }

    private void OnPatternDoubleClick(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewItem { DataContext: PatternFrequencyEntry entry } && DataContext is DocumentStatsViewModel vm)
        {
            vm.JumpToPatternCommand.Execute(entry);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
