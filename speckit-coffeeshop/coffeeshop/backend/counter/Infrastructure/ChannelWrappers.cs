using System.Threading.Channels;
using CoffeeShop.Counter.Domain;

namespace CoffeeShop.Counter.Infrastructure;

/// <summary>Named wrapper around Channel&lt;OrderDispatch&gt; for the barista dispatch lane.</summary>
public sealed class BaristaDispatchChannel(Channel<OrderDispatch> channel)
{
    public Channel<OrderDispatch> Channel { get; } = channel;
}

/// <summary>Named wrapper around Channel&lt;OrderDispatch&gt; for the kitchen dispatch lane.</summary>
public sealed class KitchenDispatchChannel(Channel<OrderDispatch> channel)
{
    public Channel<OrderDispatch> Channel { get; } = channel;
}

/// <summary>Named wrapper around Channel&lt;WorkerResult&gt; for barista completion replies.</summary>
public sealed class BaristaReplyChannel(Channel<WorkerResult> channel)
{
    public Channel<WorkerResult> Channel { get; } = channel;
}

/// <summary>Named wrapper around Channel&lt;WorkerResult&gt; for kitchen completion replies.</summary>
public sealed class KitchenReplyChannel(Channel<WorkerResult> channel)
{
    public Channel<WorkerResult> Channel { get; } = channel;
}
