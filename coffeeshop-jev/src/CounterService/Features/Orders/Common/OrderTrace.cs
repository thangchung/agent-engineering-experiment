using CounterService.Domain;
using Microsoft.Agents.AI;

namespace CounterService.Features.Orders.Common;

public sealed record AuditEntry(DateTimeOffset At, string Kind, string Detail);

/// <summary>What the front door saw and emitted for one order, kept with the order (README "Order details").
/// <see cref="Audit"/> = decisions (what the counter decided and why); <see cref="AgUi"/> = the events it streamed to the browser.</summary>
public sealed record OrderTrace(
    IReadOnlyList<string> Conversation,
    string Provider,
    string Model,
    IReadOnlyList<AuditEntry> Audit,
    IReadOnlyList<AuditEntry> AgUi);

/// <summary>Collects one run's trace. Single-threaded: one run is one sequential async iterator.</summary>
public sealed class RunTrace
{
    private readonly List<AuditEntry> _audit = [];
    private readonly List<AuditEntry> _agUi = [];

    public void Audit(string kind, string detail) => _audit.Add(new AuditEntry(DateTimeOffset.UtcNow, kind, detail));

    public void AgUi(string type, string detail) => _agUi.Add(new AuditEntry(DateTimeOffset.UtcNow, type, detail));

    /// <summary>Records an update the agent is about to stream, then returns it unchanged.</summary>
    public AgentResponseUpdate Emit(AgentResponseUpdate update, string type, string detail)
    {
        AgUi(type, detail);
        return update;
    }

    public OrderTrace Build(IReadOnlyList<string> conversation, string provider, string model) =>
        new(conversation, provider, model, [.. _audit], [.. _agUi]);

    public static string Describe(IReadOnlyList<OrderLine> lines) =>
        string.Join("; ", lines.Select(l => $"{l.Qty}× {l.Name} → {l.Station?.ToString() ?? "?"} ({l.Flag})"));
}
