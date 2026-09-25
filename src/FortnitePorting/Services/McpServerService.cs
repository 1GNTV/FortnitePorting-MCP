using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Serilog;

namespace FortnitePorting.Services;

public sealed class McpServerService : IService, IAsyncDisposable
{
    public const string DefaultBaseUrl = "http://127.0.0.1:6010";

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private WebApplication? _application;

    public string BaseUrl { get; } =
        (Environment.GetEnvironmentVariable("FORTNITE_PORTING_MCP_URL") ?? DefaultBaseUrl).TrimEnd('/');

    public string Endpoint => $"{BaseUrl}/mcp";

    public bool IsRunning => _application is not null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsDisabled())
        {
            Log.Information("Fortnite Porting MCP server disabled by FORTNITE_PORTING_MCP_DISABLED.");
            return;
        }

        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (_application is not null)
                return;

            var builder = WebApplication.CreateBuilder(Array.Empty<string>());
            builder.WebHost.UseUrls(BaseUrl);
            builder.Logging.ClearProviders();

            builder.Services
                .AddMcpServer()
                .WithHttpTransport(options =>
                {
                    options.SessionMode = HttpServerSessionMode.Stateless;
                })
                .WithTools<FortnitePorting.Mcp.FortnitePortingMcpTools>();

            var application = builder.Build();

            application.MapGet("/health", () => new
            {
                service = "FortnitePorting-MCP",
                mcp = Endpoint,
                fortniteReady = Application.AppServices.UEParse.FinishedLoading,
                registryAssets = Application.AppServices.UEParse.AssetRegistry.Count
            });

            application.MapMcp("/mcp");

            await application.StartAsync(cancellationToken);
            _application = application;

            Log.Information("Fortnite Porting MCP server listening on {Endpoint}", Endpoint);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to start Fortnite Porting MCP server.");
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            if (_application is null)
                return;

            await _application.StopAsync();
            await _application.DisposeAsync();
            _application = null;
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }

    private static bool IsDisabled()
    {
        var value = Environment.GetEnvironmentVariable("FORTNITE_PORTING_MCP_DISABLED");
        return value is not null &&
               (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
