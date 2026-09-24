using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T10: ExtractExecutor, offline against a fake IChatClient.</summary>
public class ExtractExecutorTests
{
    private static readonly IReadOnlyList<MenuItem> Menu =
    [
        new("LATTE", "Latte", 4.50m, Station.Barista),
        new("ESPRESSO", "Espresso", 3.00m, Station.Barista),
        new("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
    ];

    [Fact]
    public async Task HandleAsync_UnknownItemInResponse_IsDropped()
    {
        var fake = FakeChatClient.Returning("""{"lines":[{"name":"LATTE","qty":2},{"name":"PIZZA","qty":1}]}""");
        var draft = await RunAsync(fake);

        Assert.Single(draft.Lines);
        Assert.Equal("LATTE", draft.Lines[0].Name);
        Assert.Equal(2, draft.Lines[0].Qty);
    }

    [Fact]
    public async Task HandleAsync_DuplicateNamesDifferentCasing_AreMerged()
    {
        var fake = FakeChatClient.Returning("""{"lines":[{"name":"Latte","qty":1},{"name":"LATTE","qty":2}]}""");
        var draft = await RunAsync(fake);

        Assert.Single(draft.Lines);
        Assert.Equal(3, draft.Lines[0].Qty);
    }

    [Fact]
    public async Task HandleAsync_QuantityOverLimit_IsClampedAndFlagged()
    {
        var fake = FakeChatClient.Returning("""{"lines":[{"name":"LATTE","qty":50}]}""");
        var draft = await RunAsync(fake);

        Assert.Equal(20, draft.Lines[0].Qty);
        Assert.True(draft.Lines[0].Clamped);
    }

    [Fact]
    public async Task HandleAsync_LlmSuppliedPrice_IsIgnored_CatalogPriceUsed()
    {
        // The DTO the executor binds to has no "price" field at all, so an extra one in the
        // LLM's JSON is silently ignored - the line price always comes from the catalog.
        var fake = FakeChatClient.Returning("""{"lines":[{"name":"LATTE","qty":1,"price":0.01}]}""");
        var draft = await RunAsync(fake);

        Assert.Equal(4.50m, draft.Lines[0].Price);
    }

    [Fact]
    public async Task HandleAsync_JunkTextTwice_SendsUnclear_AfterExactlyTwoCalls()
    {
        var fake = FakeChatClient.Returning("this is not json");
        var result = await RunRawAsync(fake);

        Assert.IsType<Unclear>(result);
        Assert.Equal(2, fake.Calls.Count);
    }

    private static async Task<OrderDraft> RunAsync(FakeChatClient fake)
    {
        var result = await RunRawAsync(fake);
        return Assert.IsType<OrderDraft>(result);
    }

    private static async Task<object> RunRawAsync(FakeChatClient fake)
    {
        var agent = CounterAgentFactory.Create(fake, enableSensitiveData: false);
        var extract = new ExtractExecutor(agent, Menu);
        var sink = new SinkExecutor();
        var workflow = new WorkflowBuilder(extract).AddEdge(extract, sink).WithOutputFrom(sink).Build();

        var run = await InProcessExecution.RunAsync(workflow, new Accepted());
        var events = run.NewEvents.ToList();
        return events.OfType<WorkflowOutputEvent>().Single().Data!;
    }
}
