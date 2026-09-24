using CounterService.Domain;
using Jev.Client;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// The Split step (research.md §5.2, §4.2): one Jev call assigns every line to a station with
/// a confidence band. Jev down -> every line goes to Kitchen, flagged Review, and the order
/// still flows (research.md G4: the split fails open, unlike the Gate).
/// </summary>
[SendsMessage(typeof(SplitOrder))]
public sealed class SplitExecutor(JevClient jev) : Executor<OrderDraft, SplitOrder>("split")
{
    /// <summary>Shared-scope key DeliverExecutor reads back (with prices) for the total and reply.</summary>
    public const string SplitLinesKey = "splitLines";

    public override async ValueTask<SplitOrder> HandleAsync(OrderDraft message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        List<OrderLine> assigned;
        try
        {
            var response = await jev.AskAsync(message.Lines, StationQuestions.Build(message.Lines), cancellationToken)
                .ConfigureAwait(false);

            assigned = message.Lines.Select((line, i) =>
            {
                var answer = response.Answers[StationQuestions.QuestionId(i)];
                var (station, flag) = StationPolicy.Assign(answer.Choice!, answer.Confidence!.Value);
                return line with { Station = station, Flag = flag };
            }).ToList();
        }
        catch (JevException)
        {
            assigned = message.Lines.Select(l => l with { Station = Station.Kitchen, Flag = Flag.Review }).ToList();
        }

        await context.AddEventAsync(new SplitDone(assigned), cancellationToken).ConfigureAwait(false);
        await context.QueueStateUpdateAsync(SplitLinesKey, assigned, OrderState.ScopeName, cancellationToken).ConfigureAwait(false);

        return new SplitOrder(assigned);
    }
}
