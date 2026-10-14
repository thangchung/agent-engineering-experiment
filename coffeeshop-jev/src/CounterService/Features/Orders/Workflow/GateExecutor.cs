using CounterService.Domain;
using DecisionModel.Client;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The Gate step (research.md §5.2): one DecisionModel call decides accept / ask again / reject. Runs
/// both for the initial order text and for every human answer looped back through the
/// ask-customer <c>RequestPort</c> (research.md §5.4).
/// </summary>
[SendsMessage(typeof(Accepted))]
[SendsMessage(typeof(Unclear))]
[SendsMessage(typeof(Rejected))]
[SendsMessage(typeof(MenuRequested))]
[SendsMessage(typeof(HistoryRequested))]
public sealed class GateExecutor(DecisionModelClient decisionModel, IReadOnlyList<MenuItem> menu, OrderState? seed = null, DecisionProfile? profile = null) : Executor<string>("gate")
{
    public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var state = await context.ReadOrInitStateAsync(OrderState.Key, () => seed ?? OrderState.Empty, OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);
        var history = state.History.Append(message).ToList();

        object decision;
        int newAsks;
        try
        {
            string choice;
            double confidence;
            DecisionModelResponse response;
            if (profile is { GateMode: GateMode.Noul })
            {
                response = await decisionModel.AskAsync(GateQuestions.NoulState(message), GateQuestions.BuildNoul(menu), cancellationToken).ConfigureAwait(false);
                double Score(string id) => response.Answers[id].Noul!.Value;
                (choice, confidence) = IntentPolicy.FromNouls(Score(GateQuestions.QOrders), Score(GateQuestions.QMenu), Score(GateQuestions.QPast), Score(GateQuestions.QOffTopic));
            }
            else
            {
                response = await decisionModel.AskAsync(
                    GateQuestions.State(state.History, message),
                    GateQuestions.Build(state.History, message, menu),
                    cancellationToken).ConfigureAwait(false);
                var intent = response.Answers[GateQuestions.QIntent];
                (choice, confidence) = (intent.Choice!, intent.Confidence!.Value);
            }

            var onMenu = response.Answers[GateQuestions.QOnMenu].Noul!.Value;
            decision = IntentPolicy.Decide(choice, confidence, onMenu, state.Asks, profile);
            newAsks = decision is Unclear ? state.Asks + 1 : state.Asks;

            await context.AddEventAsync(
                new GateDecided(decision.GetType().Name, choice, confidence, onMenu, response.Model),
                cancellationToken).ConfigureAwait(false);
        }
        catch (DecisionModelException ex)
        {
            await context.AddEventAsync(new GateFailed($"HTTP {ex.StatusCode}: {(ex.Detail?.ToString() is { Length: > 0 } d ? d : ex.Message)}"), cancellationToken).ConfigureAwait(false);
            decision = new Rejected(RejectReason.DecisionModelDown, "We can't take orders right now, please try again in a moment.");
            newAsks = state.Asks;
        }

        await context.QueueStateUpdateAsync(OrderState.Key, state with { History = history, Asks = newAsks }, OrderState.ScopeName, cancellationToken)
            .ConfigureAwait(false);

        await context.SendMessageAsync(decision, cancellationToken).ConfigureAwait(false);
    }
}
