using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Workflow;
using Jev.Client;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T14: the assembled OrderWorkflow, end to end, offline (fakes only).</summary>
public class WorkflowTests
{
    private static readonly IReadOnlyList<MenuItem> Menu =
    [
        new("LATTE", "Latte", 4.50m, Station.Barista),
        new("CROISSANT", "Croissant", 3.25m, Station.Kitchen),
        new("ESPRESSO_DOUBLE", "Double espresso", 3.75m, Station.Barista),
    ];

    [Fact]
    public async Task HappyPath_LatteAndCroissant_CompletesInOrder()
    {
        var handler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[{"name":"LATTE","qty":1},{"name":"CROISSANT","qty":1}]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);

        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);
        var run = await InProcessExecution.RunAsync(workflow, "a latte and a croissant");

        var status = await run.GetStatusAsync();
        Assert.NotEqual(RunStatus.Running, status);

        var events = run.NewEvents.ToList();
        var output = FindOrderResult(events);
        Assert.Equal(OrderStatus.Completed, output.Status);

        var order = events.OfType<ExecutorInvokedEvent>().Select(e => e.ExecutorId).ToList();
        Assert.Equal(["gate", "extract", "split"], order.Take(3));
        Assert.Contains("barista", order);
        Assert.Contains("kitchen", order);
        Assert.Contains("deliver", order);
    }

    [Fact]
    public async Task Clarify_OffMenuThenAnswer_CompletesWithOneAsk()
    {
        // 1st gate call: off-menu -> Unclear. 2nd gate call (after the human's answer): accepted.
        var handler = new ScriptedJevHandler([("place_order", 0.9, 0.0), ("place_order", 0.9, 0.95)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[{"name":"LATTE","qty":1}]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);

        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);
        var run = await InProcessExecution.RunAsync(workflow, "a pizza");

        Assert.Equal(RunStatus.PendingRequests, await run.GetStatusAsync());
        var request = run.NewEvents.ToList().OfType<RequestInfoEvent>().Single().Request;

        await run.ResumeAsync([request.CreateResponse("a latte then")]);

        var output = FindOrderResult(run.NewEvents.ToList());
        Assert.Equal(OrderStatus.Completed, output.Status);
        Assert.Equal(2, handler.GateCalls); // initial turn + the human's answer
    }

    [Fact]
    public async Task AskCap_ThreeUnclearTurns_RejectsAfterExactlyTwoAsks()
    {
        var handler = new ScriptedJevHandler([("ask_menu", 0.1, 0.0), ("ask_menu", 0.1, 0.0), ("ask_menu", 0.1, 0.0)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);

        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);
        var run = await InProcessExecution.RunAsync(workflow, "hmm");

        var asks = 0;
        while (await run.GetStatusAsync() == RunStatus.PendingRequests)
        {
            var request = run.NewEvents.ToList().OfType<RequestInfoEvent>().Single().Request;
            asks++;
            await run.ResumeAsync([request.CreateResponse($"answer {asks}")]);
        }

        Assert.Equal(2, asks);
        var output = FindOrderResult(run.NewEvents.ToList());
        Assert.Equal(OrderStatus.Rejected, output.Status);
        Assert.Equal(RejectReason.AskCap, output.Reason);
    }

    [Fact]
    public async Task AskMenu_ReturnsMenuWithoutClarificationOrOrder()
    {
        var handler = new ScriptedJevHandler([("ask_menu", 0.9, 0.0)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);

        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);
        var run = await InProcessExecution.RunAsync(workflow, "show me the menu");

        var events = run.NewEvents.ToList();
        var response = Assert.IsType<MenuResponse>(events.OfType<WorkflowOutputEvent>().Single().Data);
        Assert.Equal(Menu, response.Items);
        Assert.DoesNotContain(events, e => e is RequestInfoEvent);
        Assert.DoesNotContain(events, e => e is ExecutorInvokedEvent invoked && invoked.ExecutorId == "extract");
    }

    [Fact]
    public async Task DrinksOnly_CompletesQuickly_KitchenNeverCalled()
    {
        var handler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
        var jev = NewJev(handler);
        var kitchenFake = new FakeChatClient((_, _) => throw new InvalidOperationException("kitchen must not be called"));
        var counter = CounterFake("""{"lines":[{"name":"ESPRESSO_DOUBLE","qty":1}]}""");
        var (counterAgent, baristaAgent, _) = Agents(counter);
        var kitchenAgent = KitchenAgentFactory.Create(kitchenFake, enableSensitiveData: false);

        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var run = await InProcessExecution.RunAsync(workflow, "one double espresso please");
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
        var output = FindOrderResult(run.NewEvents.ToList());
        Assert.Equal(OrderStatus.Completed, output.Status);
        Assert.Empty(kitchenFake.Calls);
    }

    [Fact]
    public async Task TwoConcurrentRuns_FromSeparateBuildCalls_BothComplete()
    {
        Task<Run> RunOrderAsync(string text)
        {
            var handler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
            var jev = NewJev(handler);
            var counter = CounterFake("""{"lines":[{"name":"LATTE","qty":1}]}""");
            var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);
            var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent); // separate Workflow per run (B1)
            return InProcessExecution.RunAsync(workflow, text).AsTask();
        }

        var results = await Task.WhenAll(RunOrderAsync("a latte"), RunOrderAsync("another latte"));

        foreach (var run in results)
        {
            var output = FindOrderResult(run.NewEvents.ToList());
            Assert.Equal(OrderStatus.Completed, output.Status);
        }
    }

    /// <summary>
    /// research.md finding (T14): <c>AgentResponseEvent</c> is itself a subclass of
    /// <c>WorkflowOutputEvent</c> (DeliverExecutor/ReplyExecutor both emit one via AddEventAsync
    /// alongside the real YieldOutputAsync). A plain <c>OfType&lt;WorkflowOutputEvent&gt;()</c>
    /// therefore matches both - filter to the exact type, not just <c>is</c>.
    /// </summary>
    private static OrderResult FindOrderResult(IReadOnlyList<WorkflowEvent> events) =>
        (OrderResult)events.Single(e => e.GetType() == typeof(WorkflowOutputEvent)).Data!;

    [Fact]
    public async Task OneSharedWorkflow_TwoConcurrentRuns_ThrowsAlreadyOwned()
    {
        var handler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[{"name":"LATTE","qty":1}]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);
        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent); // ONE instance, reused

        await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            Task.WhenAll(
                InProcessExecution.RunAsync(workflow, "a latte").AsTask(),
                InProcessExecution.RunAsync(workflow, "another latte").AsTask()));
    }

    [Fact]
    public async Task Evaluate_HasSubResultsForEveryExecutor()
    {
        var handler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
        var jev = NewJev(handler);
        var counter = CounterFake("""{"lines":[{"name":"LATTE","qty":1},{"name":"CROISSANT","qty":1}]}""");
        var (counterAgent, baristaAgent, kitchenAgent) = Agents(counter);
        var workflow = OrderWorkflow.Build(jev, Menu, counterAgent, baristaAgent, kitchenAgent);

        var run = await InProcessExecution.RunAsync(workflow, "a latte and a croissant");
        var results = await run.EvaluateAsync(
            new LocalEvaluator(EvalChecks.NonEmpty(minLength: 0)),
            includePerAgent: true);

        Assert.NotNull(results.SubResults);
        foreach (var id in new[] { "gate", "extract", "split", "barista", "kitchen", "deliver" })
        {
            Assert.True(results.SubResults!.ContainsKey(id), $"missing SubResults for '{id}'");
        }
    }

    private static JevClient NewJev(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new JevOptions());

    private static FakeChatClient CounterFake(string extractJson, string clarifyText = "Sorry, could you say that again?", string deliverText = "Thanks, your order is on its way!") =>
        new((messages, _) =>
        {
            var prompt = messages[^1].Text ?? string.Empty;
            if (prompt.Contains("Extract the final order", StringComparison.Ordinal))
            {
                return extractJson;
            }

            if (prompt.Contains("Write one short, friendly question", StringComparison.Ordinal))
            {
                return clarifyText;
            }

            return deliverText;
        });

    private static (AIAgent Counter, AIAgent Barista, AIAgent Kitchen) Agents(FakeChatClient counterFake)
    {
        return (
            CounterAgentFactory.Create(counterFake, enableSensitiveData: false),
            BaristaAgentFactory.Create(FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":1}]}"""), enableSensitiveData: false),
            KitchenAgentFactory.Create(FakeChatClient.Returning("""{"items":[{"name":"CROISSANT","qty":1}]}"""), enableSensitiveData: false));
    }
}
