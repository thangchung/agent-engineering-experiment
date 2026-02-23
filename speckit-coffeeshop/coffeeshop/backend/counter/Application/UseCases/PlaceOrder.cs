using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;

namespace CoffeeShop.Counter.Application.UseCases;

/// <summary>
/// Request DTO for placing an order.
/// </summary>
public record PlaceOrderRequest(
    string CustomerId,
    IReadOnlyList<OrderItemRequest> Items,
    string? Notes
);

/// <summary>
/// Individual line item in a PlaceOrderRequest.
/// Type matches <see cref="ItemType"/> ToString() value, e.g. "LATTE".
/// </summary>
public record OrderItemRequest(string Type, int Quantity);

/// <summary>
/// Places a new order after enforcing the pre-placement validation sequence (SC-008):
///   1. Non-empty items list                         (FR-015)
///   2. Quantity 1–5 for every line item             (FR-024)
///   3. Product-catalog reachable                    (FR-021)
///   4. Requested items available                    (FR-016)
///
/// Only stores the order after all validations pass.
/// </summary>
public sealed class PlaceOrder
{
    private readonly InMemoryCustomerStore _customerStore;
    private readonly InMemoryOrderStore _orderStore;
    private readonly IProductCatalogClient _catalogClient;

    public PlaceOrder(
        InMemoryCustomerStore customerStore,
        InMemoryOrderStore orderStore,
        IProductCatalogClient catalogClient)
    {
        _customerStore = customerStore;
        _orderStore = orderStore;
        _catalogClient = catalogClient;
    }

    public async Task<Order> ExecuteAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        // --- Step 1: Non-empty items (FR-015) ---
        if (request.Items is null || request.Items.Count == 0)
            throw new OrderValidationException("Order must contain at least one item.");

        // --- Step 2: Quantity validation 1–5 (FR-024) ---
        foreach (var item in request.Items)
        {
            if (item.Quantity < 1 || item.Quantity > 5)
                throw new OrderValidationException(
                    $"Quantity for '{item.Type}' must be between 1 and 5 (got {item.Quantity}).");
        }

        // --- Step 3: Fetch catalog — propagates ProductCatalogUnavailableException (FR-021) ---
        var catalogItems = await _catalogClient.GetMenuItemsAsync(cancellationToken);
        var byType = catalogItems.ToDictionary(
            m => m.Type.ToString(),
            m => m,
            StringComparer.OrdinalIgnoreCase);

        // --- Step 4: Item availability (FR-016) ---
        foreach (var req in request.Items)
        {
            if (!byType.TryGetValue(req.Type, out var menuItem) || !menuItem.IsAvailable)
                throw new ItemUnavailableException(
                    $"Item '{req.Type}' is currently unavailable.");
        }

        // --- Build order items ---
        var orderItems = request.Items
            .Select(req =>
            {
                var m = byType[req.Type];
                return new OrderItem(
                    m.Type,
                    m.DisplayName,
                    req.Quantity,
                    m.Price,
                    m.Price * req.Quantity);
            })
            .ToList()
            .AsReadOnly();

        var totalPrice = orderItems.Sum(i => i.LineTotal);

        // --- Pickup time (FR-008) ---
        bool hasBeverage = orderItems.Any(i => byType[i.Type.ToString()].Category == ItemCategory.Beverages);
        bool hasNonBeverage = orderItems.Any(i => byType[i.Type.ToString()].Category != ItemCategory.Beverages);
        string estimatedPickup = (hasBeverage && !hasNonBeverage)
            ? "Ready in about 5 minutes"
            : "Ready in about 10 minutes";

        // --- Generate order ID ---
        string orderId = $"ORD-{Random.Shared.Next(6000, 10000)}";
        var now = DateTimeOffset.UtcNow;

        var order = new Order(
            Id: orderId,
            CustomerId: request.CustomerId,
            Items: orderItems,
            TotalPrice: totalPrice,
            Status: OrderStatus.Confirmed,
            Notes: request.Notes,
            EstimatedPickup: estimatedPickup,
            CreatedAt: now,
            UpdatedAt: now);

        // --- Store only after all validation passes (SC-008) ---
        _orderStore.TryAdd(order);

        return order;
    }
}
