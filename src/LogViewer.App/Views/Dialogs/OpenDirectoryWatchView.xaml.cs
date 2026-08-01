using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenDirectoryWatchView : Window
{
    public OpenDirectoryWatchView()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenDirectoryWatchViewModel { IsValid: false })
        {
            MessageBox.Show(this, "Please choose a directory and a wildcard pattern.", "Open Directory (Watch)", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
