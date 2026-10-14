using CounterService.Domain;
using CounterService.Features.Orders.Common;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Workflow;

[YieldsOutput(typeof(MenuResponse))]
public sealed class ShowMenuExecutor(IReadOnlyList<MenuItem> menu) : Executor<MenuRequested>("show-menu")
{
    public override async ValueTask HandleAsync(MenuRequested message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        await context.YieldOutputAsync(new MenuResponse(menu), cancellationToken).ConfigureAwait(false);
}

/// <summary>The Rejected path (research.md §5.2): the order is over, no station is involved.</summary>
[YieldsOutput(typeof(OrderResult))]
public sealed class ReplyExecutor() : Executor<Rejected>("reply")
{
    public override async ValueTask HandleAsync(Rejected message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var result = new OrderResult
        {
            Status = OrderStatus.Rejected,
            Message = message.Message,
            Reason = message.Reason,
        };

        await context.AddEventAsync(
            new AgentResponseEvent(Id, new AgentResponse(new ChatMessage(ChatRole.Assistant, message.Message))),
            cancellationToken).ConfigureAwait(false);
        await context.YieldOutputAsync(result, cancellationToken).ConfigureAwait(false);
    }
}

[YieldsOutput(typeof(HistoryResponse))]
public sealed class ShowHistoryExecutor(OrderStore? store, string? customerId) : Executor<HistoryRequested>("show-history")
{
    public override async ValueTask HandleAsync(HistoryRequested message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<OrderRecord> orders = store is null || string.IsNullOrEmpty(customerId) ? [] : store.ListByCustomer(customerId);
        await context.YieldOutputAsync(new HistoryResponse(orders), cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Turn-boundary clarify (research.md §17.3): the question is the run's output, the answer arrives as the next run.</summary>
[YieldsOutput(typeof(ClarifyRequest))]
public sealed class AskOutputExecutor() : Executor<ClarifyRequest>("ask-output")
{
    public override async ValueTask HandleAsync(ClarifyRequest message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        await context.YieldOutputAsync(message, cancellationToken).ConfigureAwait(false);
}
