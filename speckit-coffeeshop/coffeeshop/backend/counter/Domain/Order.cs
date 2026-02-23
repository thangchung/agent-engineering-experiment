namespace CoffeeShop.Counter.Domain;

/// <summary>
/// Represents a customer order.
/// Id format: "ORD-XXXX" (runtime: ORD-{6000-9999}, seed: ORD-5001/5002/5003).
/// </summary>
public record Order(
    string Id,                          // Format: "ORD-XXXX"
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    decimal TotalPrice,                 // Sum of all LineTotal values
    OrderStatus Status,
    string? Notes,                      // Special instructions; nullable
    string? EstimatedPickup,            // Set at Confirmed; "Ready in about N minutes"
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
