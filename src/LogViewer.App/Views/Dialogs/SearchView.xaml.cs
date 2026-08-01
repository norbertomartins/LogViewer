using System.Windows;
using System.Windows.Input;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class SearchView : Window
{
    public SearchView()
    {
        InitializeComponent();
    }

    private void OnResultsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            viewModel.JumpToSelectedCommand.Execute(null);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
