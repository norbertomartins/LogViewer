using System.Windows;
using System.Windows.Controls;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;

namespace LogViewer.App.Views.Dialogs;

public partial class CustomFormatsEditorView : Window
{
    public CustomFormatsEditorView()
    {
        InitializeComponent();
    }

    /// <summary>Picking an example adds a copy of it as a new format, then resets the picker so the same example
    /// can be picked again.</summary>
    private void OnExampleSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ExamplePicker.SelectedItem is CustomLogFormat example && DataContext is CustomFormatsEditorViewModel vm)
        {
            vm.AddFromExampleCommand.Execute(example);
            ExamplePicker.SelectedItem = null;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
