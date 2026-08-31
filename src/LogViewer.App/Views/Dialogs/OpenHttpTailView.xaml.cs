using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenHttpTailView : Window
{
    public OpenHttpTailView()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenHttpTailViewModel { IsValid: false })
        {
            MessageBox.Show(this, "Please enter a valid http:// or https:// URL.", "Open HTTP Log Endpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
