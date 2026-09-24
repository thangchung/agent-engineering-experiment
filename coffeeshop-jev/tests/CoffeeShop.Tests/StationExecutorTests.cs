using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Workflow;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T12: StationExecutor, offline against a fake IChatClient.</summary>
public class StationExecutorTests
{
    [Fact]
    public async Task HandleAsync_NoLinesForThisStation_ReturnsEmpty_NoLlmCall()
    {
        var fake = FakeChatClient.Returning("""{"items":[]}""");
        var agent = KitchenAgentFactory.Create(fake, enableSensitiveData: false);
        var executor = new StationExecutor(Station.Kitchen, agent, "kitchen");

        var split = new SplitOrder([new OrderLine { Name = "LATTE", Qty = 1, Price = 4.5m, Station = Station.Barista }]);
        var ticket = await executor.HandleAsync(split, new NoopWorkflowContext());

        Assert.Empty(ticket.Items);
        Assert.False(ticket.IsFallback);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task HandleAsync_AgentThrows_ReturnsFallbackTicket_NoExceptionEscapes()
    {
        var fake = new FakeChatClient((_, _) => throw new InvalidOperationException("boom"));
        var agent = BaristaAgentFactory.Create(fake, enableSensitiveData: false);
        var executor = new StationExecutor(Station.Barista, agent, "barista");

        var lines = new List<OrderLine> { new() { Name = "LATTE", Qty = 2, Price = 4.5m, Station = Station.Barista } };
        var ticket = await executor.HandleAsync(new SplitOrder(lines), new NoopWorkflowContext());

        Assert.True(ticket.IsFallback);
        Assert.Single(ticket.Items);
    }

    [Fact]
    public async Task HandleAsync_TicketMatchesInputLinesExactly()
    {
        var fake = FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":2,"steps":["pull shot","steam milk"],"minutes":3}]}""");
        var agent = BaristaAgentFactory.Create(fake, enableSensitiveData: false);
        var executor = new StationExecutor(Station.Barista, agent, "barista");

        var lines = new List<OrderLine> { new() { Name = "LATTE", Qty = 2, Price = 4.5m, Station = Station.Barista } };
        var ticket = await executor.HandleAsync(new SplitOrder(lines), new NoopWorkflowContext());

        Assert.False(ticket.IsFallback);
        Assert.Single(ticket.Items);
        Assert.Equal("LATTE", ticket.Items[0].Name);
        Assert.Equal(2, ticket.Items[0].Qty);
    }

    [Fact]
    public async Task HandleAsync_AgentDropsAnItem_FallsBack()
    {
        // Input has 2 lines, the agent's ticket only covers 1 -> must not silently under-deliver.
        var fake = FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":1}]}""");
        var agent = BaristaAgentFactory.Create(fake, enableSensitiveData: false);
        var executor = new StationExecutor(Station.Barista, agent, "barista");

        var lines = new List<OrderLine>
        {
            new() { Name = "LATTE", Qty = 1, Price = 4.5m, Station = Station.Barista },
            new() { Name = "ESPRESSO", Qty = 1, Price = 3.0m, Station = Station.Barista },
        };
        var ticket = await executor.HandleAsync(new SplitOrder(lines), new NoopWorkflowContext());

        Assert.True(ticket.IsFallback);
    }
}
