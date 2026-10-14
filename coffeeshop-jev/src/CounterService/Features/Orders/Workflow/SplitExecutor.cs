using CounterService.Domain;
using DecisionModel.Client;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The Split step (research.md §5.2, §4.2): one DecisionModel call assigns every line to a station with
/// a confidence band. DecisionModel down -> every line goes to Kitchen, flagged Review, and the order
/// still flows (research.md G4: the split fails open, unlike the Gate).
/// </summary>
[SendsMessage(typeof(SplitOrder))]
public sealed class SplitExecutor(DecisionModelClient decisionModel, DecisionProfile? profile = null, IReadOnlyList<MenuItem>? menu = null) : Executor<OrderDraft, SplitOrder>("split")
{
    /// <summary>Shared-scope key DeliverExecutor reads back (with prices) for the total and reply.</summary>
    public const string SplitLinesKey = "splitLines";

    public override async ValueTask<SplitOrder> HandleAsync(OrderDraft message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        List<OrderLine> assigned;
        if (profile is { SplitWithModel: false } && menu is not null)
        {
            // This provider cannot route stations: the catalog already knows each item's station.
            assigned = message.Lines.Select(l => l with { Station = menu.FirstOrDefault(m => m.Id == l.Name)?.Station ?? Station.Kitchen, Flag = Flag.None }).ToList();
            await context.AddEventAsync(new SplitDone(assigned), cancellationToken).ConfigureAwait(false);
            await context.QueueStateUpdateAsync(SplitLinesKey, assigned, OrderState.ScopeName, cancellationToken).ConfigureAwait(false);
            return new SplitOrder(assigned);
        }

        try
        {
            var response = await decisionModel.AskAsync(message.Lines, StationQuestions.Build(message.Lines), cancellationToken)
                .ConfigureAwait(false);

            assigned = message.Lines.Select((line, i) =>
            {
                var answer = response.Answers[StationQuestions.QuestionId(i)];
                var (station, flag) = StationPolicy.Assign(answer.Choice!, answer.Confidence!.Value, profile);
                return line with { Station = station, Flag = flag };
            }).ToList();
        }
        catch (DecisionModelException)
        {
            assigned = message.Lines.Select(l => l with { Station = Station.Kitchen, Flag = Flag.Review }).ToList();
        }

        await context.AddEventAsync(new SplitDone(assigned), cancellationToken).ConfigureAwait(false);
        await context.QueueStateUpdateAsync(SplitLinesKey, assigned, OrderState.ScopeName, cancellationToken).ConfigureAwait(false);

        return new SplitOrder(assigned);
    }
}
