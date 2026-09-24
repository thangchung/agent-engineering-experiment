using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T13: DeliverExecutor + ReplyExecutor.</summary>
public class DeliverAndReplyExecutorTests
{
    [Fact]
    public async Task Deliver_TwoTickets_TotalIsExactDecimalSum()
    {
        var fake = FakeChatClient.Returning("Thanks, your order is on its way!");
        var agent = CounterAgentFactory.Create(fake, enableSensitiveData: false);
        var deliver = new DeliverExecutor(agent);
        var context = new NoopWorkflowContext();

        var lines = new List<OrderLine>
        {
            new() { Name = "LATTE", Qty = 2, Price = 4.50m, Station = Station.Barista },
            new() { Name = "CROISSANT", Qty = 1, Price = 3.25m, Station = Station.Kitchen },
        };
        await context.QueueStateUpdateAsync(SplitExecutor.SplitLinesKey, lines, OrderState.ScopeName);

        // The barrier delivers one ticket per station - call HandleAsync once for each (T13 AC5:
        // the fan-in target's HandleAsync is invoked once per message, not once with a list).
        await deliver.HandleAsync(StationTicket.Empty(Station.Barista), context);
        await deliver.HandleAsync(StationTicket.Empty(Station.Kitchen), context);

        var result = Assert.IsType<OrderResult>(Assert.Single(context.Outputs));
        Assert.Equal(12.25m, result.Total);
        Assert.Equal(OrderStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Deliver_ClampedLine_PromptMentionsMaximum()
    {
        var fake = FakeChatClient.Returning("ok");
        var agent = CounterAgentFactory.Create(fake, enableSensitiveData: false);
        var deliver = new DeliverExecutor(agent);
        var context = new NoopWorkflowContext();

        var lines = new List<OrderLine> { new() { Name = "LATTE", Qty = 20, Price = 4.50m, Clamped = true } };
        await context.QueueStateUpdateAsync(SplitExecutor.SplitLinesKey, lines, OrderState.ScopeName);

        await deliver.HandleAsync(StationTicket.Empty(Station.Barista), context);
        await deliver.HandleAsync(StationTicket.Empty(Station.Kitchen), context);

        var prompt = fake.Calls.Single().Messages.Single().Text;
        Assert.Contains("maximum", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deliver_EmitsAgentResponseEvent()
    {
        var fake = FakeChatClient.Returning("all set!");
        var agent = CounterAgentFactory.Create(fake, enableSensitiveData: false);
        var deliver = new DeliverExecutor(agent);
        var context = new NoopWorkflowContext();
        await context.QueueStateUpdateAsync(SplitExecutor.SplitLinesKey, new List<OrderLine>(), OrderState.ScopeName);

        await deliver.HandleAsync(StationTicket.Empty(Station.Barista), context);
        await deliver.HandleAsync(StationTicket.Empty(Station.Kitchen), context);

        Assert.Contains(context.Events, e => e is AgentResponseEvent);
    }

    [Fact]
    public async Task Reply_EmitsAgentResponseEvent_AndYieldsRejectedResult()
    {
        var reply = new ReplyExecutor();
        var context = new NoopWorkflowContext();

        await reply.HandleAsync(new Rejected(RejectReason.JevDown, "We can't take orders right now, please try again in a moment."), context);

        Assert.Contains(context.Events, e => e is AgentResponseEvent);
        var result = Assert.IsType<OrderResult>(Assert.Single(context.Outputs));
        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Equal(RejectReason.JevDown, result.Reason);
        Assert.Contains("can't take orders", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
