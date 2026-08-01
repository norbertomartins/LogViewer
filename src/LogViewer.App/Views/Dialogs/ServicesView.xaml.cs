using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class ServicesView : Window
{
    public ServicesView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
