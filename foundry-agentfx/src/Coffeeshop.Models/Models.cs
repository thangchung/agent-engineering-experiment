namespace Coffeeshop.Models;

public record MenuItem(string Id, string Name, string Category, decimal Price, string Description = "");

public record Customer(string Id, string Name, string Email, string Phone);

public record OrderItem(string MenuItemId, string MenuItemName, int Quantity, decimal UnitPrice);

public record OrderResult(
    string OrderId,
    string CustomerId,
    string CustomerName,
    List<OrderItem> Items,
    decimal Total,
    DateTimeOffset PlacedAt);
