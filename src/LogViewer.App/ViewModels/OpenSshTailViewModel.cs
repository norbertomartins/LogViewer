using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.Core.Tailing;

namespace LogViewer.App.ViewModels;

public sealed partial class OpenSshTailViewModel : ObservableObject
{
    private readonly IReadOnlyList<SshConfigHost> _configHosts = SshConfigParser.ReadDefault();

    /// <summary>Literal aliases found in <c>~/.ssh/config</c>, offered as a "saved host" picker.</summary>
    public IReadOnlyList<string> AvailableAliases => SshConfigParser.ListAliases(_configHosts);

    public bool HasConfigAliases => AvailableAliases.Count > 0;

    [ObservableProperty]
    private string? _selectedAlias;

    /// <summary>Pre-fills Host/Port/Username/PrivateKeyPath from the chosen <c>~/.ssh/config</c> alias.
    /// Only overwrites fields the config actually defines for that alias (or a matching <c>Host *</c>
    /// fallback) — Command/Password/Passphrase/Fingerprint are never part of the SSH config format, so
    /// they're left untouched.</summary>
    partial void OnSelectedAliasChanged(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var resolved = SshConfigParser.Resolve(_configHosts, value);
        if (resolved is null)
        {
            return;
        }

        Host = resolved.HostName ?? value;
        if (resolved.Port is { } port)
        {
            Port = port;
        }

        if (!string.IsNullOrEmpty(resolved.User))
        {
            Username = resolved.User;
        }

        if (!string.IsNullOrEmpty(resolved.IdentityFile))
        {
            PrivateKeyPath = resolved.IdentityFile;
        }
    }

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
