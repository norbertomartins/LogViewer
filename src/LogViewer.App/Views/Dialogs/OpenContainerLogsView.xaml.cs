using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenContainerLogsView : Window
{
    public OpenContainerLogsView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (DataContext is OpenContainerLogsViewModel vm)
            {
                await vm.RefreshCommand.ExecuteAsync(null);
            }
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
