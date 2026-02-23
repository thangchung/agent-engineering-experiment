using System.Threading.Channels;
using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using CoffeeShop.Counter.Workers;
using Moq;
using Xunit;

namespace CoffeeShop.Tests.Unit.Workers;

/// <summary>
/// TDD tests for KitchenWorker.
/// These tests MUST fail before T034 implementation is written.
///
/// KitchenWorker has identical channel-loop semantics to BaristaWorker
/// but processes Food + Others items routed to kitchen.
/// </summary>
public sealed class KitchenWorkerTests
{
    private static OrderDispatch MakeDispatch(string orderId = "ORD-6100") =>
        new(orderId, "C-1002",
            new List<OrderItem>
            {
                new(ItemType.CROISSANT, "CROISSANT", 1, 3.25m, 3.25m),
                new(ItemType.MUFFIN,    "MUFFIN",    1, 3.00m, 3.00m),
            }.AsReadOnly(),
            $"{orderId}-{Guid.NewGuid():N}");

    private static (KitchenDispatchChannel dispatch, KitchenReplyChannel reply)
        MakeChannels()
    {
        var dispatch = new KitchenDispatchChannel(
            Channel.CreateUnbounded<OrderDispatch>(
                new UnboundedChannelOptions { SingleWriter = false, SingleReader = true }));
        var reply = new KitchenReplyChannel(
            Channel.CreateUnbounded<WorkerResult>(
                new UnboundedChannelOptions { SingleWriter = false, SingleReader = true }));
        return (dispatch, reply);
    }

    // -----------------------------------------------------------------------
    // Happy path — agent succeeds → WorkerResult(Success=true)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ValidDispatch_AgentSucceeds_WritesSuccessResult()
    {
        var (dispatchCh, replyCh) = MakeChannels();
        var agentMock = new Mock<IWorkerAgent>();
        agentMock.Setup(a => a.RunAsync(It.IsAny<OrderDispatch>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new KitchenWorker(dispatchCh, replyCh, agentMock.Object);

        var dispatch = MakeDispatch("ORD-6100");
        await dispatchCh.Channel.Writer.WriteAsync(dispatch, cts.Token);
        dispatchCh.Channel.Writer.TryComplete();

        await worker.ExecuteAsync(cts.Token);

        var result = await replyCh.Channel.Reader.ReadAsync(cts.Token);
        Assert.True(result.Success);
        Assert.Equal(dispatch.CorrelationId, result.CorrelationId);
        Assert.Null(result.ErrorMessage);
    }

    // -----------------------------------------------------------------------
    // Exception path — agent throws → WorkerResult(Success=false)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AgentThrows_WritesFailureResult()
    {
        var (dispatchCh, replyCh) = MakeChannels();
        var agentMock = new Mock<IWorkerAgent>();
        agentMock.Setup(a => a.RunAsync(It.IsAny<OrderDispatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("kitchen fire"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new KitchenWorker(dispatchCh, replyCh, agentMock.Object);

        var dispatch = MakeDispatch("ORD-6101");
        await dispatchCh.Channel.Writer.WriteAsync(dispatch, cts.Token);
        dispatchCh.Channel.Writer.TryComplete();

        await worker.ExecuteAsync(cts.Token);

        var result = await replyCh.Channel.Reader.ReadAsync(cts.Token);
        Assert.False(result.Success);
        Assert.Contains("kitchen fire", result.ErrorMessage);
    }
}
