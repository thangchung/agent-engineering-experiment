using CoffeeshopCli.Models;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// Minimal MCP client contract used by submit handlers and skill runner.
/// </summary>
public interface IMcpClient
{
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default);

    Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default);
}
