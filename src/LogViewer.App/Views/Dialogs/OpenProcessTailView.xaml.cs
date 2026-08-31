using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenProcessTailView : Window
{
    public OpenProcessTailView() => InitializeComponent();

    private void OnPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is OpenProcessTailViewModel vm && e.AddedItems.Count == 1 && e.AddedItems[0] is string preset)
        {
            vm.ApplyPreset(preset);
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenProcessTailViewModel { IsValid: false })
        {
            MessageBox.Show(this, "Please enter an executable to run.", "Open Command Output", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
