using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class SimilarBlockView : Window
{
    public SimilarBlockView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
