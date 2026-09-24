using CounterService.Domain;
using Jev.Client;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The Gate step (research.md §5.2): one Jev call decides accept / ask again / reject. Runs
/// both for the initial order text and for every human answer looped back through the
/// ask-customer <c>RequestPort</c> (research.md §5.4).
/// </summary>
[SendsMessage(typeof(Accepted))]
[SendsMessage(typeof(Unclear))]
[SendsMessage(typeof(Rejected))]
[SendsMessage(typeof(MenuRequested))]
public sealed class GateExecutor(JevClient jev, IReadOnlyList<MenuItem> menu) : Executor<string>("gate")
{
    public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var state = await context.ReadOrInitStateAsync(OrderState.Key, () => OrderState.Empty, OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);
        var history = state.History.Append(message).ToList();

        object decision;
        int newAsks;
        try
        {
            var response = await jev.AskAsync(
                new { history = state.History, latest = message },
                GateQuestions.Build(state.History, message, menu),
                cancellationToken).ConfigureAwait(false);

            var intent = response.Answers[GateQuestions.QIntent];
            var onMenu = response.Answers[GateQuestions.QOnMenu];

            decision = IntentPolicy.Decide(intent.Choice!, intent.Confidence!.Value, onMenu.Noul!.Value, state.Asks);
            newAsks = decision is Unclear ? state.Asks + 1 : state.Asks;

            await context.AddEventAsync(
                new GateDecided(decision.GetType().Name, intent.Choice!, intent.Confidence!.Value, onMenu.Noul!.Value, response.Model),
                cancellationToken).ConfigureAwait(false);
        }
        catch (JevException)
        {
            decision = new Rejected(RejectReason.JevDown, "We can't take orders right now, please try again in a moment.");
            newAsks = state.Asks;
        }

        await context.QueueStateUpdateAsync(OrderState.Key, state with { History = history, Asks = newAsks }, OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);

        await context.SendMessageAsync(decision, cancellationToken).ConfigureAwait(false);
    }
}
