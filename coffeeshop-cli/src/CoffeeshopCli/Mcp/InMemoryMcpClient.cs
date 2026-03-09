using CoffeeshopCli.Models;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// Local fallback MCP client used for development/testing until stdio transport is wired.
/// </summary>
public sealed class InMemoryMcpClient : IMcpClient
{
    private static readonly List<MenuItem> Menu = new()
    {
        new MenuItem { ItemType = ItemType.LATTE, Name = "Latte", Category = "Coffee", Price = 4.50m },
        new MenuItem { ItemType = ItemType.ESPRESSO, Name = "Espresso", Category = "Coffee", Price = 3.00m },
        new MenuItem { ItemType = ItemType.CAPPUCCINO, Name = "Cappuccino", Category = "Coffee", Price = 4.75m }
    };

    private static readonly List<Customer> Customers = new()
    {
        new Customer
        {
            CustomerId = "C-1001",
            Name = "Alice Smith",
            Email = "alice@example.com",
            Phone = "+1-555-0100",
            Tier = CustomerTier.Gold,
            AccountCreated = new DateOnly(2023, 1, 10)
        }
    };

    public Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<MenuItem>>(Menu);
    }

    public Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Customer>>(Customers);
    }

    public Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(order);
    }
}
