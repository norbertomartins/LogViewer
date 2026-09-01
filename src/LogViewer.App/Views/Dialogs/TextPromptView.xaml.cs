using System.Windows;

namespace LogViewer.App.Views.Dialogs;

public partial class TextPromptView : Window
{
    public TextPromptView(string title, string prompt, string? initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initial ?? string.Empty;
        Loaded += (_, _) =>
        {
            InputBox.SelectAll();
            InputBox.Focus();
        };
    }

    public string? Value { get; private set; }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        Value = InputBox.Text;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
