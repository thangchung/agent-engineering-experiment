using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Application.UseCases;

/// <summary>
/// Returns the complete product catalog by calling the product-catalog MCP service.
/// Returns ALL items including unavailable ones — filtering is the UI's responsibility.
/// Propagates <see cref="ProductCatalogUnavailableException"/> as 503 to the caller.
/// </summary>
public sealed class GetMenu
{
    private readonly IProductCatalogClient _catalog;

    public GetMenu(IProductCatalogClient catalog) => _catalog = catalog;

    /// <summary>
    /// Fetches all menu items from the product-catalog service.
    /// </summary>
    /// <exception cref="ProductCatalogUnavailableException">
    /// Thrown when the product-catalog is unreachable or returns an error.
    /// Maps to HTTP 503.
    /// </exception>
    public Task<IReadOnlyList<MenuItem>> ExecuteAsync(CancellationToken ct = default)
        => _catalog.GetMenuItemsAsync(ct);
}
