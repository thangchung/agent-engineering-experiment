using System.Collections.Concurrent;
using CounterService.Domain;

namespace CounterService.Features.Orders.Common;

public sealed record OrderRecord(int Number, DateTimeOffset CreatedAt, OrderResult Result, string? CustomerId = null, OrderTrace? Trace = null);

/// <summary>
/// research.md §14 Q7: "in memory db" - an incrementing order number and the last N results,
/// for the `GET /orders` board. No persistence across restarts (ponytail: add EF Core InMemory
/// or a real store only if you need it to survive a restart or a real query).
/// </summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, OrderRecord> _orders = [];
    private int _nextNumber;

    /// <summary>Reserves a number before the order is stored, so the front door can show it in the events it streams first.</summary>
    public int NextNumber() => Interlocked.Increment(ref _nextNumber);

    public OrderRecord Add(OrderResult result, string? customerId = null, OrderTrace? trace = null, int? number = null)
    {
        var n = number ?? NextNumber();
        var record = new OrderRecord(n, DateTimeOffset.UtcNow, result, customerId, trace);
        _orders[n] = record;
        return record;
    }

    public OrderRecord? Get(int number) => _orders.GetValueOrDefault(number);

    public IReadOnlyList<OrderRecord> ListNewestFirst() =>
        _orders.Values.OrderByDescending(o => o.Number).ToList();

    public IReadOnlyList<OrderRecord> ListByCustomer(string customerId) =>
        _orders.Values.Where(o => o.CustomerId == customerId).OrderByDescending(o => o.Number).ToList();
}
