using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>
/// Stub IWorkerAgent used in the counter's DI container at runtime.
/// In a full implementation this would invoke the MAF BaristaAgent/KitchenAgent
/// via RunStreamingAsync. For MVP, the worker loop runs without actual agent
/// side-effects — order fulfilment is confirmed at placement time.
/// </summary>
public sealed class NoOpWorkerAgent : IWorkerAgent
{
    private readonly ILogger<NoOpWorkerAgent> _logger;

    public NoOpWorkerAgent(ILogger<NoOpWorkerAgent> logger) => _logger = logger;

    public Task RunAsync(OrderDispatch dispatch, CancellationToken ct)
    {
        _logger.LogInformation(
            "Worker agent processing order {OrderId} (correlation {CorrelationId})",
            dispatch.OrderId,
            dispatch.CorrelationId);
        return Task.CompletedTask;
    }
}
