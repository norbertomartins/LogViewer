using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class HighlightRuleEditorView : Window
{
    public HighlightRuleEditorView()
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
