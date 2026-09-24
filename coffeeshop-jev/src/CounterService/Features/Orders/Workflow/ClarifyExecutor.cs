using CounterService.Domain;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Workflow;

/// <summary>The Clarify step (research.md §5.2): writes one short question for the human.
/// The ask counter itself is incremented by <see cref="GateExecutor"/>, which is the only place
/// that decides Unclear vs give-up.</summary>
[SendsMessage(typeof(ClarifyRequest))]
public sealed class ClarifyExecutor(AIAgent counterAgent, IReadOnlyList<MenuItem> menu) : Executor<Unclear, ClarifyRequest>("clarify")
{
    public override async ValueTask<ClarifyRequest> HandleAsync(Unclear message, IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        var prompt = ClarifyPrompt.Build(message.Reason, menu);
        var response = await counterAgent.RunAsync([new ChatMessage(ChatRole.User, prompt)], cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new ClarifyRequest(response.Text, menu.Select(m => m.DisplayName).ToList());
    }
}
