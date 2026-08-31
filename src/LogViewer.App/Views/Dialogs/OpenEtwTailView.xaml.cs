using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenEtwTailView : Window
{
    public OpenEtwTailView() => InitializeComponent();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenEtwTailViewModel { IsValid: false })
        {
            MessageBox.Show(this, "Please enter an ETW provider name or GUID.", "Open ETW Provider", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
