namespace LogViewer.Core.Tailing;

/// <summary>
/// Minimal reader for an OpenSSH client config file (<c>~/.ssh/config</c>) — just enough to let the SSH
/// tail dialog offer the user's already-configured aliases and pre-fill HostName/Port/User/IdentityFile
/// from them. Only <c>Host</c>, <c>HostName</c>, <c>Port</c>, <c>User</c> and <c>IdentityFile</c> are
/// understood; every other directive (ProxyJump, Include, Match, etc.) is ignored. Pattern negation
/// (<c>!pattern</c>) is not supported.
/// </summary>
public static class SshConfigParser
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");

    /// <summary>Reads and parses <see cref="DefaultPath"/>, or returns an empty list if it doesn't exist
    /// or can't be read.</summary>
    public static IReadOnlyList<SshConfigHost> ReadDefault()
    {
        try
        {
            return File.Exists(DefaultPath) ? Parse(File.ReadAllText(DefaultPath)) : [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<SshConfigHost> Parse(string configText)
    {
        var hosts = new List<SshConfigHost>();
        List<string>? patterns = null;
        string? hostName = null;
        int? port = null;
        string? user = null;
        string? identityFile = null;

        void Flush()
        {
            if (patterns is { Count: > 0 })
            {
                hosts.Add(new SshConfigHost(patterns, hostName, port, user, identityFile));
            }

            hostName = null;
            port = null;
            user = null;
            identityFile = null;
        }

        foreach (var rawLine in configText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var (keyword, value) = SplitKeyword(line);
            if (keyword is null || value.Length == 0)
            {
                continue;
            }

            if (string.Equals(keyword, "Host", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                patterns = [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
                continue;
            }

            if (patterns is null)
            {
                // Directive before any Host line — global defaults aren't supported, skip.
                continue;
            }

            switch (keyword.ToUpperInvariant())
            {
                case "HOSTNAME":
                    hostName ??= value;
                    break;
                case "PORT":
                    if (int.TryParse(value, out var parsedPort))
                    {
                        port ??= parsedPort;
                    }

                    break;
                case "USER":
                    user ??= value;
                    break;
                case "IDENTITYFILE":
                    identityFile ??= ExpandTilde(value);
                    break;
            }
        }

        Flush();
        return hosts;
    }

    /// <summary>Literal aliases only (skips wildcard-only entries like <c>Host *</c>) — what a "saved
    /// host" picker should list.</summary>
    public static IReadOnlyList<string> ListAliases(IReadOnlyList<SshConfigHost> hosts) =>
        hosts.SelectMany(h => h.Patterns)
            .Where(p => !p.Contains('*') && !p.Contains('?'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Resolves the effective HostName/Port/User/IdentityFile for <paramref name="alias"/> by
    /// applying OpenSSH's "first obtained value per keyword wins" rule across every stanza whose pattern
    /// matches, in file order (so a literal <c>Host myserver</c> stanza takes precedence over a later
    /// <c>Host *</c> fallback). Returns null if no stanza matches at all.</summary>
    public static SshConfigHost? Resolve(IReadOnlyList<SshConfigHost> hosts, string alias)
    {
        string? hostName = null;
        int? port = null;
        string? user = null;
        string? identityFile = null;
        var matched = false;

        foreach (var host in hosts)
        {
            if (!host.Patterns.Any(p => MatchesPattern(p, alias)))
            {
                continue;
            }

            matched = true;
            hostName ??= host.HostName;
            port ??= host.Port;
            user ??= host.User;
            identityFile ??= host.IdentityFile;
        }

        return matched ? new SshConfigHost([alias], hostName, port, user, identityFile) : null;
    }

    private static bool MatchesPattern(string pattern, string alias)
    {
        if (pattern == "*")
        {
            return true;
        }

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return string.Equals(pattern, alias, StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace(@"\*", ".*", StringComparison.Ordinal)
            .Replace(@"\?", ".", StringComparison.Ordinal) + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(alias, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string ExpandTilde(string path) =>
        path is "~" || path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~').TrimStart('/'))
            : path;

    private static (string? Keyword, string Value) SplitKeyword(string line)
    {
        var separatorIndex = line.IndexOfAny([' ', '\t', '=']);
        if (separatorIndex < 0)
        {
            return (null, string.Empty);
        }

        var keyword = line[..separatorIndex];
        var value = line[(separatorIndex + 1)..].Trim().Trim('"');
        return (keyword, value);
    }
}
