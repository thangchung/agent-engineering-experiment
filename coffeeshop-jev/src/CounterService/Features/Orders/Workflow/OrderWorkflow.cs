using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using Jev.Client;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Builds the order-placement workflow (research.md §5.2). Ask-customer human-in-the-loop is a
/// <see cref="RequestPort{TRequest,TResponse}"/> (research.md §5.4).
///
/// research.md B1: a MAF <see cref="Microsoft.Agents.AI.Workflows.Workflow"/> can only be owned
/// by one runner at a time - call <see cref="Build"/> once per order, never share an instance
/// across concurrent runs.
/// </summary>
public static class OrderWorkflow
{
    public const string AskPortId = "ask-customer";

    public static Microsoft.Agents.AI.Workflows.Workflow Build(
        JevClient jev,
        IReadOnlyList<MenuItem> menu,
        AIAgent counterAgent,
        AIAgent baristaAgent,
        AIAgent kitchenAgent)
    {
        var gate = new GateExecutor(jev, menu);
        var clarify = new ClarifyExecutor(counterAgent, menu);
        var extract = new ExtractExecutor(counterAgent, menu);
        var split = new SplitExecutor(jev);
        var barista = new StationExecutor(Station.Barista, baristaAgent, AgentKeys.Barista);
        var kitchen = new StationExecutor(Station.Kitchen, kitchenAgent, AgentKeys.Kitchen);
        var deliver = new DeliverExecutor(counterAgent);
        var reply = new ReplyExecutor();
        var showMenu = new ShowMenuExecutor(menu);
        var ask = RequestPort.Create<ClarifyRequest, string>(AskPortId);

        return new WorkflowBuilder(gate)
            .WithName("order-placement")
            .AddSwitch(gate, sb => sb
                .AddCase<object>(m => m is Accepted, [extract])
                .AddCase<object>(m => m is Unclear, [clarify])
                .AddCase<object>(m => m is MenuRequested, [showMenu])
                .WithDefault([reply]))
            .AddSwitch(extract, sb => sb
                .AddCase<object>(m => m is OrderDraft, [split])
                .WithDefault([clarify]))
            .AddEdge(clarify, ask)
            .AddEdge(ask, gate)
            .AddFanOutEdge(split, [barista, kitchen])
            .AddFanInBarrierEdge([barista, kitchen], deliver)
            .WithOutputFrom(deliver, reply, showMenu)
            .Build();
    }
}
