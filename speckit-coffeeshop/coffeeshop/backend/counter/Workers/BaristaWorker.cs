using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using Microsoft.Extensions.Hosting;

namespace CoffeeShop.Counter.Workers;

/// <summary>
/// IHostedService that reads OrderDispatch from the BaristaDispatchChannel,
/// invokes IWorkerAgent.RunAsync, and writes WorkerResult to BaristaReplyChannel.
/// Catches all exceptions and writes WorkerResult(Success=false).
/// </summary>
public sealed class BaristaWorker : IHostedService
{
    private readonly BaristaDispatchChannel _dispatchChannel;
    private readonly BaristaReplyChannel _replyChannel;
    private readonly IWorkerAgent _agent;
    private Task? _runTask;

    public BaristaWorker(
        BaristaDispatchChannel dispatchChannel,
        BaristaReplyChannel replyChannel,
        IWorkerAgent agent)
    {
        _dispatchChannel = dispatchChannel;
        _replyChannel = replyChannel;
        _agent = agent;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = ExecuteAsync(cancellationToken);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runTask is not null)
            await _runTask.WaitAsync(cancellationToken);
    }

    public async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var dispatch in _dispatchChannel.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            WorkerResult result;
            try
            {
                await _agent.RunAsync(dispatch, stoppingToken);
                result = new WorkerResult(dispatch.CorrelationId, Success: true, ErrorMessage: null);
            }
            catch (Exception ex)
            {
                result = new WorkerResult(dispatch.CorrelationId, Success: false, ErrorMessage: ex.Message);
            }

            await _replyChannel.Channel.Writer.WriteAsync(result, stoppingToken);
        }
    }
}
