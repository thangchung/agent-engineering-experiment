using CoffeeshopCli.Errors;
using CoffeeshopCli.Models;

namespace CoffeeshopCli.Mcp;

/// <summary>
/// Wraps MCP client calls and normalizes unexpected failures as McpError.
/// </summary>
public sealed class McpClientWrapper : IMcpClient
{
    private readonly IMcpClient _inner;

    public McpClientWrapper(IMcpClient inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.GetMenuAsync(cancellationToken);
        }
        catch (CliError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpError($"Failed to fetch menu: {ex.Message}");
        }
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.GetCustomersAsync(cancellationToken);
        }
        catch (CliError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpError($"Failed to fetch customers: {ex.Message}");
        }
    }

    public async Task<Order> CreateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.CreateOrderAsync(order, cancellationToken);
        }
        catch (CliError)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpError($"Failed to create order: {ex.Message}");
        }
    }
}
