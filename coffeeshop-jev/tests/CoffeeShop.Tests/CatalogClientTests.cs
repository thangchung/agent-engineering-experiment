using System.ComponentModel;
using CounterService.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;

namespace CoffeeShop.Tests;

/// <summary>
/// tasks.md T06 AC4: an in-process (real, not mocked) MCP server whose get_menu tool returns
/// malformed content, wired through TestServer so CatalogClient talks real HTTP/MCP end to end.
/// </summary>
public class CatalogClientTests
{
    [Fact]
    public async Task GetMenuAsync_MalformedToolResult_ThrowsCatalogUnavailableException_AndCacheStaysUntouched()
    {
        using var host = await BuildBrokenCatalogHostAsync();
        var testClient = host.GetTestClient();
        var httpClientFactory = new SingleClientFactory(testClient);
        var catalogClient = new CatalogClient(httpClientFactory, NullLogger<CatalogClient>.Instance);

        // First call: no cache yet, the tool result is malformed -> must throw, not return garbage.
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalogClient.GetMenuAsync());

        // Second call: still no cache (the failed call must not have poisoned it with partial data).
        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalogClient.GetMenuAsync());
    }

    private static async Task<IHost> BuildBrokenCatalogHostAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddMcpServer().WithHttpTransport().WithTools<BrokenMenuTools>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapMcp("/mcp"));
                });
            })
            .StartAsync();

        return host;
    }

    [McpServerToolType]
    public sealed class BrokenMenuTools
    {
        [McpServerTool(Name = "get_menu")]
        [Description("Deliberately returns malformed content for the test.")]
        public static string GetMenu() => "this is not valid json for a menu";
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
