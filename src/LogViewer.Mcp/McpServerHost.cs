using LogViewer.Core.Analysis;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.Documents;
using LogViewer.Core.Search;
using LogViewer.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LogViewer.Mcp;

/// <summary>Owns an embedded Kestrel/ASP.NET Core host exposing the MCP tools over Streamable HTTP,
/// bound to localhost. Started/stopped alongside the WPF app's own lifetime; failures never throw out
/// of <see cref="StartAsync"/> so a port conflict or similar can't crash the host app.</summary>
public sealed class McpServerHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    public McpServerHost(
        McpServerSettings settings,
        IOpenDocumentCatalog documentCatalog,
        IFullTextSearchService searchService,
        IBlockScanService blockScanService,
        ISimilarBlockFinder similarBlockFinder,
        IPatternFrequencyAnalyzer patternFrequencyAnalyzer,
        ILineWindowReader lineWindowReader)
    {
        ResponseLimits.Configure(settings.MaxResultsPerCall, settings.MaxLineTextLength);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://{settings.BindAddress}:{settings.Port}");

        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(documentCatalog);
        builder.Services.AddSingleton(searchService);
        builder.Services.AddSingleton(blockScanService);
        builder.Services.AddSingleton(similarBlockFinder);
        builder.Services.AddSingleton(patternFrequencyAnalyzer);
        builder.Services.AddSingleton(lineWindowReader);

        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<LogDiscoveryTools>()
            .WithTools<LogSearchTools>()
            .WithTools<LogPatternTools>()
            .WithTools<LogBlockTools>();

        _app = builder.Build();

        if (settings.RequireApiKeyHeader)
        {
            _app.UseMiddleware<ApiKeyMiddleware>();
        }

        _app.MapMcp();
    }

    /// <summary>Null on success; the exception message when Kestrel failed to bind/start (e.g. the
    /// configured port is already in use) — the caller decides how to surface this non-fatally.</summary>
    public string? StartupError { get; private set; }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _app.StartAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            StartupError = ex.Message;
            return false;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => _app.StopAsync(cancellationToken);

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}
