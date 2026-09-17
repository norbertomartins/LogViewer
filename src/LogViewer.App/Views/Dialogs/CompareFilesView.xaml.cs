using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class CompareFilesView : Window
{
    public CompareFilesView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
