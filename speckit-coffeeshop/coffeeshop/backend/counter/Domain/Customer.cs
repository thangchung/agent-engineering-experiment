namespace CoffeeShop.Counter.Domain;

/// <summary>
/// Represents an identified customer in the coffeeshop system.
/// Lookup keys: Id, Email (case-insensitive), or via Order.CustomerId.
/// </summary>
public record Customer(
    string Id,               // Format: "C-XXXX" (e.g., "C-1001")
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    CustomerTier Tier,
    DateOnly AccountCreated
);
