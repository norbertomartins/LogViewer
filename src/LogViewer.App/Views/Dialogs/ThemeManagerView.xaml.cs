using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class ThemeManagerView : Window
{
    public ThemeManagerView()
    {
        InitializeComponent();
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
