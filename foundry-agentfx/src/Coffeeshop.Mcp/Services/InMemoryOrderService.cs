namespace Coffeeshop.Mcp.Services;

using Coffeeshop.Models;

public sealed class InMemoryOrderService : IOrderService
{
    private readonly IMenuService _menu;
    private readonly IAuditService _audit;

    public InMemoryOrderService(IMenuService menu, IAuditService audit)
    {
        _menu = menu;
        _audit = audit;
    }

    public async Task<OrderResult> SubmitAsync(string customerId, List<OrderItem> items, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            throw new ArgumentException("Customer ID is required.", nameof(customerId));
        if (items == null || items.Count == 0)
            throw new ArgumentException("Order must contain at least one item.", nameof(items));

        var allItems = await _menu.GetAllItemsAsync(ct);
        var resolvedItems = new List<OrderItem>();

        foreach (var item in items)
        {
            var menuItem = allItems.FirstOrDefault(m =>
                string.Equals(m.Id, item.MenuItemId, StringComparison.OrdinalIgnoreCase));
            if (menuItem is null)
                throw new InvalidOperationException($"Menu item '{item.MenuItemId}' not found.");
            if (item.Quantity <= 0)
                throw new ArgumentException($"Quantity for '{item.MenuItemId}' must be positive.");

            resolvedItems.Add(new OrderItem(menuItem.Id, menuItem.Name, item.Quantity, menuItem.Price));
        }

        var total = resolvedItems.Sum(i => i.UnitPrice * i.Quantity);
        var orderId = $"ORD-{DateTimeOffset.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

        var result = new OrderResult(
            OrderId: orderId,
            CustomerId: customerId,
            CustomerName: customerId,
            Items: resolvedItems,
            Total: total,
            PlacedAt: DateTimeOffset.UtcNow);

        await _audit.WriteOrderAuditAsync(result, ct);
        return result;
    }
}
