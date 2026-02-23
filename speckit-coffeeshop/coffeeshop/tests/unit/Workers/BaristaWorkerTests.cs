using System.Threading.Channels;
using CoffeeShop.Counter.Application.Ports;
using CoffeeShop.Counter.Domain;
using CoffeeShop.Counter.Infrastructure;
using CoffeeShop.Counter.Workers;
using Moq;
using Xunit;

namespace CoffeeShop.Tests.Unit.Workers;

/// <summary>
/// TDD tests for BaristaWorker.
/// These tests MUST fail before T033 implementation is written.
///
/// Tests verify the worker channel-loop contract:
///   - Reads OrderDispatch from BaristaDispatchChannel
///   - Invokes IWorkerAgent.RunAsync(dispatch)
///   - Writes WorkerResult(Success=true) to BaristaReplyChannel
///   - On exception: writes WorkerResult(Success=false, ErrorMessage=...)
/// </summary>
public sealed class BaristaWorkerTests
{
    private static OrderDispatch MakeDispatch(string orderId = "ORD-6001") =>
        new(orderId, "C-1001",
            new List<OrderItem>
            {
                new(ItemType.LATTE, "LATTE", 1, 4.50m, 4.50m)
            }.AsReadOnly(),
            $"{orderId}-{Guid.NewGuid():N}");

    private static (BaristaDispatchChannel dispatch, BaristaReplyChannel reply)
        MakeChannels()
    {
        var dispatch = new BaristaDispatchChannel(
            Channel.CreateUnbounded<OrderDispatch>(
                new UnboundedChannelOptions { SingleWriter = false, SingleReader = true }));
        var reply = new BaristaReplyChannel(
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
        var worker = new BaristaWorker(dispatchCh, replyCh, agentMock.Object);

        // Write a dispatch message
        var dispatch = MakeDispatch("ORD-6001");
        await dispatchCh.Channel.Writer.WriteAsync(dispatch, cts.Token);
        dispatchCh.Channel.Writer.TryComplete(); // signal end-of-stream

        // Run the worker loop
        await worker.ExecuteAsync(cts.Token);

        // Read the reply
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
            .ThrowsAsync(new InvalidOperationException("Agent blew up"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new BaristaWorker(dispatchCh, replyCh, agentMock.Object);

        var dispatch = MakeDispatch("ORD-6002");
        await dispatchCh.Channel.Writer.WriteAsync(dispatch, cts.Token);
        dispatchCh.Channel.Writer.TryComplete();

        await worker.ExecuteAsync(cts.Token);

        var result = await replyCh.Channel.Reader.ReadAsync(cts.Token);
        Assert.False(result.Success);
        Assert.Equal(dispatch.CorrelationId, result.CorrelationId);
        Assert.Contains("Agent blew up", result.ErrorMessage);
    }

    // -----------------------------------------------------------------------
    // Multiple dispatches are processed sequentially
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultipleDispatches_AllProcessed()
    {
        var (dispatchCh, replyCh) = MakeChannels();
        var agentMock = new Mock<IWorkerAgent>();
        agentMock.Setup(a => a.RunAsync(It.IsAny<OrderDispatch>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var worker = new BaristaWorker(dispatchCh, replyCh, agentMock.Object);

        var d1 = MakeDispatch("ORD-6010");
        var d2 = MakeDispatch("ORD-6011");
        await dispatchCh.Channel.Writer.WriteAsync(d1, cts.Token);
        await dispatchCh.Channel.Writer.WriteAsync(d2, cts.Token);
        dispatchCh.Channel.Writer.TryComplete();

        await worker.ExecuteAsync(cts.Token);

        var r1 = await replyCh.Channel.Reader.ReadAsync(cts.Token);
        var r2 = await replyCh.Channel.Reader.ReadAsync(cts.Token);

        Assert.True(r1.Success);
        Assert.True(r2.Success);
    }
}
