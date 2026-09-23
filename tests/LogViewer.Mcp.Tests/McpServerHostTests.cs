using System.Net;
using System.Net.Sockets;
using LogViewer.Core.Analysis;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.Documents;
using LogViewer.Core.Search;
using ModelContextProtocol.Client;

namespace LogViewer.Mcp.Tests;

/// <summary>Goes through the real Streamable HTTP transport to check which tools an agent actually sees.</summary>
public sealed class McpServerHostTests
{
    private sealed class EmptyCatalog : IOpenDocumentCatalog, IDocumentAnnotationWriter
    {
        public IReadOnlyList<OpenDocumentInfo> GetOpenDocuments() => [];

        public string? AddBookmark(string sourcePath, long lineNumber) => null;

        public string? AddNote(string sourcePath, long lineNumber, string lineText, string note) => null;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteTools_AreOnlyListed_WhenAnnotationWritesAreAllowed(bool allowWrites)
    {
        var tools = await ListToolNamesAsync(new McpServerSettings { Port = FreePort(), AllowAnnotationWrites = allowWrites });

        Assert.Contains("logs_get_notes", tools);
        Assert.Equal(allowWrites, tools.Contains("logs_add_bookmark"));
        Assert.Equal(allowWrites, tools.Contains("logs_add_note"));
    }

    private static async Task<IReadOnlyList<string>> ListToolNamesAsync(McpServerSettings settings)
    {
        var catalog = new EmptyCatalog();
        var scan = new FileBlockScanService();
        await using var host = new McpServerHost(
            settings, catalog, new FileFullTextSearchService(), scan, new SimilarBlockFinder(scan),
            new FilePatternFrequencyAnalyzer(), new FileLineWindowReader(), catalog);
        Assert.True(await host.StartAsync(CancellationToken.None), host.StartupError);

        var transport = new HttpClientTransport(new HttpClientTransportOptions { Endpoint = new Uri($"http://127.0.0.1:{settings.Port}/") });
        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        await host.StopAsync(CancellationToken.None);
        return [.. tools.Select(t => t.Name)];
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
