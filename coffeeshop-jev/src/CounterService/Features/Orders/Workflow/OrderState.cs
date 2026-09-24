namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Per-order state shared across executors, kept in the workflow's named "order" scope
/// (research.md B2: the default scope is per-executor, so a named scope is required for
/// anything more than one executor needs to see).
/// </summary>
public sealed record OrderState(IReadOnlyList<string> History, int Asks)
{
    public const string ScopeName = "order";
    public const string Key = "state";

    public static OrderState Empty { get; } = new([], 0);
}
