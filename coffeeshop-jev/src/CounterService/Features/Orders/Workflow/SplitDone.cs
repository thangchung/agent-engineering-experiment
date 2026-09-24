using CounterService.Domain;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>Emitted by <see cref="SplitExecutor"/> after every split, for the UI and audit
/// (research.md §5.2 rule 4).</summary>
public sealed class SplitDone(IReadOnlyList<OrderLine> lines) : WorkflowEvent(lines);
