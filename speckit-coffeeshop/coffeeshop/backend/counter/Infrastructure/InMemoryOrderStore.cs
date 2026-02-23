using System.Collections.Concurrent;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>
/// Thread-safe in-memory order store backed by ConcurrentDictionary.
/// Supports lookup by OrderId and index from CustomerId → list of OrderIds.
///
/// INVARIANT: Direct dict[key] = value is PROHIBITED. All mutations must use
/// TryAdd or TryUpdate (compare-and-swap semantics) to prevent lost updates.
/// </summary>
public sealed class InMemoryOrderStore
{
    private readonly ConcurrentDictionary<string, Order> _orders = new(StringComparer.Ordinal);
    // CustomerId → List<OrderId>
    private readonly ConcurrentDictionary<string, List<string>> _customerIndex = new(StringComparer.Ordinal);
    private readonly object _indexLock = new();

    /// <summary>
    /// Add an order to the store atomically.
    /// Returns false if an order with the same Id already exists.
    /// </summary>
    public bool TryAdd(Order order)
    {
        if (!_orders.TryAdd(order.Id, order))
            return false;

        lock (_indexLock)
        {
            var list = _customerIndex.GetOrAdd(order.CustomerId, _ => new List<string>());
            list.Add(order.Id);
        }

        return true;
    }

    /// <summary>
    /// Atomically replace an existing order using compare-and-swap.
    /// Returns false if the order was not found or if it was concurrently modified
    /// (expectedVersion must match the current stored order reference).
    /// </summary>
    public bool TryUpdate(Order expectedVersion, Order newVersion)
    {
        if (expectedVersion.Id != newVersion.Id)
            throw new ArgumentException("Cannot change order Id during update.");

        return _orders.TryUpdate(expectedVersion.Id, newVersion, expectedVersion);
    }

    /// <summary>Fetch an order by its Id. Returns false if not found.</summary>
    public bool TryGetById(string orderId, out Order? order)
    {
        var found = _orders.TryGetValue(orderId, out var o);
        order = o;
        return found;
    }

    /// <summary>
    /// Return all orders for a given customer, sorted most-recent first.
    /// An empty list is a valid result (not an error) — FR-014.
    /// </summary>
    public IReadOnlyList<Order> GetByCustomerId(string customerId)
    {
        List<string> orderIds;
        lock (_indexLock)
        {
            if (!_customerIndex.TryGetValue(customerId, out var ids))
                return Array.Empty<Order>();
            orderIds = new List<string>(ids); // snapshot under lock
        }

        var orders = orderIds
            .Select(id => _orders.TryGetValue(id, out var o) ? o : null)
            .Where(o => o is not null)
            .Cast<Order>()
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        return orders.AsReadOnly();
    }
}
