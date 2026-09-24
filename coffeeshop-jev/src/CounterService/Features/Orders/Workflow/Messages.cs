using System.Text.Json;
using CounterService.Domain;

namespace CounterService.Features.Orders.Workflow;

// research.md §5.2 rule 2: every message overrides ToString() to return JSON, so the eval
// harness's per-executor EvalItems (which read Data.ToString()) see real structured data
// instead of a record's default "Messages.Accepted { }" dump.

/// <summary>The gate accepted the order text as a genuine, on-menu order.</summary>
public sealed record Accepted
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

/// <summary>The gate (or the split's grounding guard) needs another turn from the customer.</summary>
public sealed record Unclear(string Reason)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

/// <summary>The order is over - never sent to a station.</summary>
public sealed record Rejected(RejectReason Reason, string Message)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

public sealed record MenuRequested;

public sealed record MenuResponse(IReadOnlyList<MenuItem> Items);

/// <summary>A question for the human, carried through the <c>RequestPort</c> (research.md §5.4).</summary>
public sealed record ClarifyRequest(string Question, IReadOnlyList<string> Menu)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

/// <summary>The extracted, catalog-validated order lines, before the station split.</summary>
public sealed record OrderDraft(IReadOnlyList<OrderLine> Lines)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}

/// <summary>The order lines after the station split - each line now has a Station and a Flag.</summary>
public sealed record SplitOrder(IReadOnlyList<OrderLine> Lines)
{
    public override string ToString() => JsonSerializer.Serialize(this);
}
