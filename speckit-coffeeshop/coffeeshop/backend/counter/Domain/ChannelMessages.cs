namespace CoffeeShop.Counter.Domain;

/// <summary>
/// Message dispatched to barista or kitchen workers via Channel&lt;T&gt;.
/// CorrelationId is generated as $"{order.Id}-{Guid.NewGuid():N}" for tracing.
/// </summary>
public record OrderDispatch(
    string OrderId,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,
    string CorrelationId
);

/// <summary>
/// Reply written back to the counter by a worker upon completion or failure.
/// </summary>
public record WorkerResult(
    string CorrelationId,
    bool Success,
    string? ErrorMessage
);
