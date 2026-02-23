using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Application.Ports;

/// <summary>
/// Abstraction over a MAF worker agent invocation.
/// Implementations: real MAF agent (prod), mock (unit tests).
///
/// Encapsulates the RunStreamingAsync call for barista or kitchen agents
/// so workers can be unit-tested without a full MAF runtime.
/// </summary>
public interface IWorkerAgent
{
    /// <summary>
    /// Process a dispatch message for this worker.
    /// Streaming output is consumed internally for OTel spans only.
    /// Throws on irrecoverable failure (callers catch and write WorkerResult).
    /// </summary>
    Task RunAsync(OrderDispatch dispatch, CancellationToken ct = default);
}
