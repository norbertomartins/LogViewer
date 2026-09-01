using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenMergedSourcesView : Window
{
    public OpenMergedSourcesView()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenMergedSourcesViewModel { IsValid: false })
        {
            MessageBox.Show(
                this,
                "Add at least two files in total (folders count as their matching files).",
                "Open Merged Files / Folders",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
