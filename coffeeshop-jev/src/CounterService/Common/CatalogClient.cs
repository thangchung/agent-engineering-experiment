using System.Text.Json;
using CounterService.Domain;
using ModelContextProtocol.Client;

namespace CounterService.Common;

/// <summary>
/// Fetches the menu from ProductCatalogService's MCP <c>get_menu</c> tool
/// (research.md §6, §12.3 U1). Keeps a last-good cache so a transient catalog outage does not
/// take the whole counter down; only a stopped catalog with no prior successful fetch fails.
/// </summary>
public sealed class CatalogClient(IHttpClientFactory httpClientFactory, ILogger<CatalogClient> logger) : ICatalogClient, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<MenuItem>? _lastGood;

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var fresh = await FetchAsync(cancellationToken).ConfigureAwait(false);
            _lastGood = fresh;
            return fresh;
        }
        // A caller-driven cancellation (e.g. the request was aborted) must still propagate;
        // everything else - including an HttpClient.Timeout, which also throws
        // OperationCanceledException - falls back to the cache.
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            if (_lastGood is { Count: > 0 } cached)
            {
                logger.LogWarning(ex, "Catalog fetch failed - serving the last-good cached menu ({Count} items)", cached.Count);
                return cached;
            }

            throw new CatalogUnavailableException("The catalog is unavailable and no cached menu exists yet.", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<MenuItem>> FetchAsync(CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient("catalog");
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri("http://catalog/mcp") },
            httpClient);

        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = await client.CallToolAsync("get_menu", cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = result.Content
            .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Select(c => c.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new CatalogUnavailableException("get_menu returned no content.");
        }

        var items = JsonSerializer.Deserialize<List<CatalogMenuItemDto>>(text, Json)
            ?? throw new CatalogUnavailableException("get_menu returned unparseable content.");

        return items
            .Select(i => new MenuItem(
                i.Id,
                i.DisplayName,
                i.PriceUsd,
                string.Equals(i.Station, "Barista", StringComparison.OrdinalIgnoreCase) ? Station.Barista : Station.Kitchen))
            .ToList();
    }

    private sealed record CatalogMenuItemDto(string Id, string DisplayName, decimal PriceUsd, string Station);

    public void Dispose() => _lock.Dispose();
}
