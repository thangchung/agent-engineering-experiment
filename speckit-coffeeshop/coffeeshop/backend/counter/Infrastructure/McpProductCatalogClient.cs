using CoffeeShop.Counter.Application;
using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>
/// Typed HttpClient that calls the product-catalog service REST API.
/// Base URL is injected by Aspire service discovery via the Aspire resource name "product-catalog".
/// Per-call timeout is enforced at the HttpClient level (5 seconds).
///
/// Maps product-catalog MenuItemDto → counter domain MenuItem on return.
/// </summary>
public sealed class McpProductCatalogClient : IProductCatalogClient
{
    private readonly HttpClient _http;
    private readonly ILogger<McpProductCatalogClient> _logger;

    public McpProductCatalogClient(
        HttpClient http,
        ILogger<McpProductCatalogClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MenuItem>> GetMenuItemsAsync(
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetFromJsonAsync<CatalogMenuResponse>(
                "/api/menu", ct);

            if (response?.Items is null)
                throw new ProductCatalogUnavailableException(
                    "Menu is currently unavailable — please try again shortly.");

            return response.Items
                .Select(ToMenuItem)
                .ToList()
                .AsReadOnly();
        }
        catch (ProductCatalogUnavailableException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Product catalog is unreachable.");
            throw new ProductCatalogUnavailableException(
                "Menu is currently unavailable — please try again shortly.");
        }
    }

    /// <inheritdoc/>
    public async Task<MenuItem?> GetItemByTypeAsync(
        string type,
        CancellationToken ct = default)
    {
        try
        {
            // Reuse GetMenuItems and filter locally; avoids a second endpoint.
            var all = await GetMenuItemsAsync(ct);
            return all.FirstOrDefault(m =>
                m.Type.ToString().Equals(type, StringComparison.OrdinalIgnoreCase));
        }
        catch (ProductCatalogUnavailableException)
        {
            throw;
        }
    }

    // -------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------

    private static MenuItem ToMenuItem(CatalogMenuItemDto dto) =>
        new(
            Enum.Parse<ItemType>(dto.Type, ignoreCase: true),
            dto.DisplayName,
            Enum.Parse<ItemCategory>(dto.Category, ignoreCase: true),
            dto.Price,
            dto.IsAvailable);

    // -------------------------------------------------------------------
    // Local DTOs matching product-catalog REST response
    // -------------------------------------------------------------------

    private sealed record CatalogMenuResponse(
        [property: JsonPropertyName("items")] IReadOnlyList<CatalogMenuItemDto> Items);

    private sealed record CatalogMenuItemDto(
        [property: JsonPropertyName("type")]        string  Type,
        [property: JsonPropertyName("displayName")] string  DisplayName,
        [property: JsonPropertyName("category")]    string  Category,
        [property: JsonPropertyName("price")]       decimal Price,
        [property: JsonPropertyName("isAvailable")] bool    IsAvailable);
}
