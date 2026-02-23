using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Application.Ports;

/// <summary>
/// Abstraction over the product-catalog MCP client.
/// Implementations: McpProductCatalogClient (prod), mock (unit tests).
///
/// On timeout or connection failure, implementations MUST throw
/// <see cref="ProductCatalogUnavailableException"/>.
/// </summary>
public interface IProductCatalogClient
{
    /// <summary>Returns all menu items (including unavailable ones — never filter).</summary>
    Task<IReadOnlyList<MenuItem>> GetMenuItemsAsync(CancellationToken ct = default);

    /// <summary>Returns a single item by type, or null if not found.</summary>
    Task<MenuItem?> GetItemByTypeAsync(string type, CancellationToken ct = default);
}
