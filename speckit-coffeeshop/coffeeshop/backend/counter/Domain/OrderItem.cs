namespace CoffeeShop.Counter.Domain;

/// <summary>
/// Represents a single line item in an order.
/// Quantity must be between 1 and 5 (FR-024).
/// </summary>
public record OrderItem(
    ItemType Type,
    string DisplayName,
    int Quantity,         // 1–5 (FR-024)
    decimal UnitPrice,
    decimal LineTotal     // = UnitPrice * Quantity
);
