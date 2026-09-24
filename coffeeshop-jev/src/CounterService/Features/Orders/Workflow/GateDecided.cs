using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Emitted by <see cref="GateExecutor"/> after every Jev call, carrying the raw answer for the
/// UI and for audit (research.md §5.2 rule 4, §11.4 G5).
/// </summary>
public sealed class GateDecided(string decisionType, string intentChoice, double intentConfidence, double onMenuNoul, string jevModel)
    : WorkflowEvent(new
    {
        decisionType,
        intentChoice,
        intentConfidence,
        onMenuNoul,
        jevModel,
    });
