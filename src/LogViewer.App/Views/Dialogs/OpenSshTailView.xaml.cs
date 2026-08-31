using System.Windows;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Views.Dialogs;

public partial class OpenSshTailView : Window
{
    public OpenSshTailView() => InitializeComponent();

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenSshTailViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    private void OnPassphraseChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenSshTailViewModel vm)
        {
            vm.PrivateKeyPassphrase = PassphraseBox.Password;
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenSshTailViewModel { IsValid: false })
        {
            MessageBox.Show(this, "Please fill in host, username, a command, and either a password or a private key.",
                "Open SSH Log Tail", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
