using CounterService.Domain;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The fan-in barrier's target (research.md §5.2): called once per station ticket. Accumulates
/// in the shared "order" scope until both barista and kitchen have reported (research.md B4:
/// every station always sends exactly one ticket, even an empty one, so this always completes),
/// then writes the final reply and yields the <see cref="OrderResult"/>.
/// </summary>
[YieldsOutput(typeof(OrderResult))]
public sealed class DeliverExecutor(AIAgent counterAgent) : Executor<StationTicket>("deliver")
{
    private const string TicketsKey = "tickets";

    public override async ValueTask HandleAsync(StationTicket message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var tickets = await context.ReadOrInitStateAsync(TicketsKey, () => new List<StationTicket>(), OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);
        tickets = [.. tickets, message];
        await context.QueueStateUpdateAsync(TicketsKey, tickets, OrderState.ScopeName, cancellationToken).ConfigureAwait(false);

        // Exactly one ticket per station (barista, kitchen) - wait until both have reported.
        if (tickets.Count < 2)
        {
            return;
        }

        var lines = await context.ReadOrInitStateAsync(SplitExecutor.SplitLinesKey, () => new List<OrderLine>(), OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);
        var total = lines.Sum(l => l.Price * l.Qty);
        var anyClamped = lines.Any(l => l.Clamped);

        var prompt = DeliverPrompt.Build(lines, total, anyClamped);
        var response = await counterAgent.RunAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var result = new OrderResult
        {
            Status = OrderStatus.Completed,
            Tickets = tickets,
            Total = total,
            Message = response.Text,
        };

        await context.AddEventAsync(new AgentResponseEvent(Id, response), cancellationToken).ConfigureAwait(false);
        await context.YieldOutputAsync(result, cancellationToken).ConfigureAwait(false);
    }
}
