using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public class SshConfigParserTests
{
    private const string SampleConfig = """
        # comment line, ignored
        Host prod-web
            HostName 10.0.1.5
            User deploy
            Port 2222
            IdentityFile ~/.ssh/prod_key

        Host staging
            HostName staging.example.com
            User ubuntu

        Host *
            User fallback-user
            Port 22
        """;

    [Fact]
    public void Parse_ReadsHostNameUserPortIdentityFile_PerStanza()
    {
        var hosts = SshConfigParser.Parse(SampleConfig);

        Assert.Equal(3, hosts.Count);
        var prod = hosts[0];
        Assert.Equal(["prod-web"], prod.Patterns);
        Assert.Equal("10.0.1.5", prod.HostName);
        Assert.Equal("deploy", prod.User);
        Assert.Equal(2222, prod.Port);
        Assert.EndsWith("prod_key", prod.IdentityFile);
    }

    [Fact]
    public void ListAliases_SkipsWildcardOnlyStanzas()
    {
        var hosts = SshConfigParser.Parse(SampleConfig);

        var aliases = SshConfigParser.ListAliases(hosts);

        Assert.Equal(["prod-web", "staging"], aliases);
    }

    [Fact]
    public void Resolve_LiteralAlias_UsesItsOwnValuesOverWildcardFallback()
    {
        var hosts = SshConfigParser.Parse(SampleConfig);

        var resolved = SshConfigParser.Resolve(hosts, "prod-web");

        Assert.NotNull(resolved);
        Assert.Equal("10.0.1.5", resolved!.HostName);
        Assert.Equal("deploy", resolved.User);
        Assert.Equal(2222, resolved.Port);
    }

    [Fact]
    public void Resolve_FallsBackToWildcardStanza_ForFieldsNotSetOnLiteralAlias()
    {
        var hosts = SshConfigParser.Parse(SampleConfig);

        var resolved = SshConfigParser.Resolve(hosts, "staging");

        Assert.NotNull(resolved);
        Assert.Equal("staging.example.com", resolved!.HostName);
        Assert.Equal("ubuntu", resolved.User); // literal stanza wins over the "Host *" fallback
        Assert.Equal(22, resolved.Port); // only defined on "Host *"
    }

    [Fact]
    public void Resolve_UnknownAlias_ReturnsNull()
    {
        var hosts = SshConfigParser.Parse("Host known\n    HostName 1.2.3.4\n");

        Assert.Null(SshConfigParser.Resolve(hosts, "nope"));
    }

    [Fact]
    public void Parse_EmptyText_ReturnsEmptyList()
    {
        Assert.Empty(SshConfigParser.Parse(string.Empty));
    }

}
