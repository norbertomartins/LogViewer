using System.Windows;
using LogViewer.App.ViewModels;
using Microsoft.Win32;

namespace LogViewer.App.Views.Dialogs;

public partial class ExternalToolEditorView : Window
{
    public ExternalToolEditorView()
    {
        InitializeComponent();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExternalToolEditorViewModel { SelectedTool: { } tool })
        {
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            tool.ExecutablePath = dialog.FileName;
        }
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
