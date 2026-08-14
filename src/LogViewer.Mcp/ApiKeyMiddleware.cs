using LogViewer.Core.Configuration;
using Microsoft.AspNetCore.Http;

namespace LogViewer.Mcp;

/// <summary>Rejects requests missing a matching <c>X-LogViewer-Mcp-Key</c> header, only active when
/// <see cref="McpServerSettings.RequireApiKeyHeader"/> is enabled — an extra guard for setups where
/// binding beyond loopback is intentional.</summary>
public sealed class ApiKeyMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-LogViewer-Mcp-Key";

    public async Task InvokeAsync(HttpContext context, McpServerSettings settings)
    {
        var provided = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(settings.ApiKey) || !string.Equals(provided, settings.ApiKey, StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
