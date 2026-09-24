using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;
using Jev.Client;
using Microsoft.Agents.AI.Workflows;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T09: GateExecutor, all offline against a stub Jev HttpMessageHandler.</summary>
public class GateExecutorTests
{
    private static readonly IReadOnlyList<MenuItem> Menu =
    [
        new("LATTE", "Latte", 4.50m, Station.Barista),
        new("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
    ];

    [Fact]
    public async Task HandleAsync_PlaceOrderOnMenu_SendsAccepted_AndEmitsGateDecided()
    {
        var jev = NewJevClient(CannedResponse("place_order", 0.9, true, 0.95));
        var workflow = BuildSingleExecutorWorkflow(new GateExecutor(jev, Menu));

        var run = await InProcessExecution.RunAsync(workflow, "2 lattes and a croissant");

        // Run.NewEvents drains an internal queue on enumeration (it is NOT a repeatable
        // snapshot like Run.OutgoingEvents) - materialize it once, then query the list.
        var events = run.NewEvents.ToList();

        var output = events.OfType<WorkflowOutputEvent>().Single();
        Assert.IsType<Accepted>(output.Data);

        var decided = events.OfType<GateDecided>().Single();
        Assert.NotNull(decided);
    }

    [Fact]
    public async Task HandleAsync_RequestBody_HasLatestAndHistorySeparated()
    {
        JsonNode? capturedBody = null;
        var jev = NewJevClient(async request =>
        {
            var text = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            capturedBody = text is null ? null : JsonNode.Parse(text);
            return JsonHttpResponse(CannedResponse("place_order", 0.9, true, 0.95));
        });

        var workflow = BuildSingleExecutorWorkflow(new GateExecutor(jev, Menu));
        await InProcessExecution.RunAsync(workflow, "2 lattes and a croissant");

        Assert.NotNull(capturedBody);
        Assert.Equal("2 lattes and a croissant", capturedBody!["state"]!["latest"]!.GetValue<string>());
        Assert.Empty(capturedBody["state"]!["history"]!.AsArray());
    }

    [Fact]
    public async Task HandleAsync_JevReturns503_SendsRejectedJevDown_NoExceptionEscapes()
    {
        var jev = NewJevClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("""{"detail":"overloaded"}""", Encoding.UTF8, "application/json"),
        }));

        var workflow = BuildSingleExecutorWorkflow(new GateExecutor(jev, Menu));
        var run = await InProcessExecution.RunAsync(workflow, "2 lattes");

        var output = run.NewEvents.ToList().OfType<WorkflowOutputEvent>().Single();
        var rejected = Assert.IsType<Rejected>(output.Data);
        Assert.Equal(RejectReason.JevDown, rejected.Reason);
    }

    [Fact]
    public async Task HandleAsync_HistoryWrittenToOrderScope_IsReadableByAnotherExecutor()
    {
        var jev = NewJevClient(CannedResponse("ask_menu", 0.9, false, 0.0));
        var gate = new GateExecutor(jev, Menu);
        var probe = new ScopeProbeExecutor();
        var workflow = new WorkflowBuilder(gate)
            .AddEdge(gate, probe)
            .WithOutputFrom(probe)
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, "what do you have?");

        var output = run.NewEvents.ToList().OfType<WorkflowOutputEvent>().Single();
        var state = Assert.IsType<OrderState>(output.Data);
        Assert.Single(state.History);
        Assert.Equal("what do you have?", state.History[0]);
    }

    private static Workflow BuildSingleExecutorWorkflow(GateExecutor gate)
    {
        var sink = new SinkExecutor();
        return new WorkflowBuilder(gate)
            .AddEdge(gate, sink)
            .WithOutputFrom(sink)
            .Build();
    }

    private static JevClient NewJevClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        return new JevClient(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new JevOptions());
    }

    private static JevClient NewJevClient(string cannedJson) =>
        NewJevClient(_ => Task.FromResult(JsonHttpResponse(cannedJson)));

    private static string CannedResponse(string intentChoice, double intentConfidence, bool onMenu, double onMenuNoul) => $$"""
        {
          "model": "jev-test",
          "answers": {
            "intent": {"type":"choice","choice":"{{intentChoice}}","probabilities":{},"confidence":{{intentConfidence}}},
            "on_menu": {"type":"noul","noul":{{onMenuNoul}}}
          },
          "usage": {"input_tokens":1,"output_tokens":1}
        }
        """;

    private static HttpResponseMessage JsonHttpResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    /// <summary>Reads OrderState back out of the "order" scope and yields it as output - proves
    /// research.md B2's fix (a named scope, not the executor-default one) actually works.</summary>
    private sealed class ScopeProbeExecutor() : Executor<object, object>("probe")
    {
        public override async ValueTask<object> HandleAsync(object message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
            await context.ReadOrInitStateAsync(OrderState.Key, () => OrderState.Empty, OrderState.ScopeName, cancellationToken).ConfigureAwait(false);
    }
}
