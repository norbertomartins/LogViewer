namespace LogViewer.Core.Tailing;

/// <summary>One <c>Host</c> stanza from an OpenSSH client config file, holding only the directives the
/// SSH tail dialog cares about. <see cref="Patterns"/> holds every space-separated pattern on the
/// <c>Host</c> line (e.g. <c>"prod-* staging"</c> becomes two patterns).</summary>
public sealed record SshConfigHost(
    IReadOnlyList<string> Patterns,
    string? HostName,
    int? Port,
    string? User,
    string? IdentityFile);
