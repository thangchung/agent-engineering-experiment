using CoffeeshopCli.Errors;
using CoffeeshopCli.Mcp;
using CoffeeshopCli.Models;
using CoffeeshopCli.Validation;

namespace CoffeeshopCli.Services;

/// <summary>
/// Handles simplified order submission by resolving item names/prices and calculating totals.
/// </summary>
public sealed class OrderSubmitHandler
{
    private readonly IMcpClient _mcpClient;
    private readonly OrderValidator _orderValidator;

    public OrderSubmitHandler(IMcpClient mcpClient)
    {
        _mcpClient = mcpClient;
        _orderValidator = new OrderValidator();
    }

    public async Task<Order> SubmitAsync(SimplifiedOrderInput input, CancellationToken cancellationToken = default)
    {
        var customers = await _mcpClient.GetCustomersAsync(cancellationToken);
        var customer = customers.FirstOrDefault(c => c.CustomerId.Equals(input.CustomerId, StringComparison.OrdinalIgnoreCase));
        if (customer is null)
        {
            throw new ValidationError($"unknown customer id: {input.CustomerId}");
        }

        var menu = await _mcpClient.GetMenuAsync(cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var item in input.Items)
        {
            var matched = menu.FirstOrDefault(m => m.ItemType == item.ItemType);
            if (matched is null)
            {
                throw new ValidationError($"unknown item type: {item.ItemType}");
            }

            orderItems.Add(new OrderItem
            {
                ItemType = matched.ItemType,
                Name = matched.Name,
                Qty = item.Qty,
                Price = matched.Price
            });
        }

        var total = orderItems.Sum(i => i.Price * i.Qty);

        var order = new Order
        {
            OrderId = string.IsNullOrWhiteSpace(input.OrderId) ? GenerateOrderId() : input.OrderId,
            CustomerId = customer.CustomerId,
            Status = OrderStatus.Pending,
            PlacedAt = DateTime.UtcNow,
            Total = total,
            Items = orderItems,
            Notes = new List<OrderNote>()
        };

        var validationError = _orderValidator.Validate(order);
        if (validationError is not null)
        {
            throw validationError;
        }

        return await _mcpClient.CreateOrderAsync(order, cancellationToken);
    }

    private static string GenerateOrderId()
    {
        var suffix = Random.Shared.Next(1000, 9999);
        return $"ORD-{suffix}";
    }
}

/// <summary>
/// Input contract used by models submit for simplified order payloads.
/// </summary>
public sealed record SimplifiedOrderInput
{
    public string? OrderId { get; init; }
    public required string CustomerId { get; init; }
    public required List<SimplifiedOrderItemInput> Items { get; init; }
}

/// <summary>
/// Simplified item row for order submission.
/// </summary>
public sealed record SimplifiedOrderItemInput
{
    public required ItemType ItemType { get; init; }
    public required int Qty { get; init; }
}
