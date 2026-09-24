using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests.Fakes;

/// <summary>Test-only: yields whatever it receives as the workflow's output, so a single
/// executor under test can be wired into a minimal 2-node workflow and inspected via
/// <c>WorkflowOutputEvent</c> instead of re-building the whole graph.</summary>
public sealed class SinkExecutor() : Executor<object, object>("sink")
{
    public override ValueTask<object> HandleAsync(object message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(message);
}
