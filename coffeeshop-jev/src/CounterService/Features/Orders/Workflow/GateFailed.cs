using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>Emitted when the Gate's decision-model call fails (the order is then refused, fail-closed), so the reason lands in the audit log instead of only "ordering was offline".</summary>
public sealed class GateFailed(string detail) : WorkflowEvent(detail);
