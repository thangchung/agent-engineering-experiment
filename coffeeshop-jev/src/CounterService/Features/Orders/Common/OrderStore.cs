using System.Collections.Concurrent;
using CounterService.Domain;

namespace CounterService.Features.Orders.Common;

public sealed record OrderRecord(int Number, DateTimeOffset CreatedAt, OrderResult Result);

/// <summary>
/// research.md §14 Q7: "in memory db" - an incrementing order number and the last N results,
/// for the `GET /orders` board. No persistence across restarts (ponytail: add EF Core InMemory
/// or a real store only if you need it to survive a restart or a real query).
/// </summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, OrderRecord> _orders = [];
    private int _nextNumber;

    public OrderRecord Add(OrderResult result)
    {
        var number = Interlocked.Increment(ref _nextNumber);
        var record = new OrderRecord(number, DateTimeOffset.UtcNow, result);
        _orders[number] = record;
        return record;
    }

    public IReadOnlyList<OrderRecord> ListNewestFirst() =>
        _orders.Values.OrderByDescending(o => o.Number).ToList();
}
