namespace CoffeeShop.Counter.Domain;

/// <summary>
/// Represents a menu item sourced from the product-catalog MCP Server.
/// Reference data — immutable at runtime.
/// </summary>
public record MenuItem(
    ItemType Type,
    string DisplayName,
    ItemCategory Category,
    decimal Price,
    bool IsAvailable
);
