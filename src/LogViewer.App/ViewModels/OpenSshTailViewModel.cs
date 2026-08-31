using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenSshTailViewModel : ObservableObject
{
    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _privateKeyPath = string.Empty;

    [ObservableProperty]
    private string _privateKeyPassphrase = string.Empty;

    [ObservableProperty]
    private string _command = "tail -n 200 -F /var/log/syslog";

    [ObservableProperty]
    private string _hostKeyFingerprintSha256 = string.Empty;

    [ObservableProperty]
    private bool _acceptAnyHostKey;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Host)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Command)
        && Port is > 0 and <= 65535
        && (!string.IsNullOrEmpty(Password) || !string.IsNullOrWhiteSpace(PrivateKeyPath));
}
