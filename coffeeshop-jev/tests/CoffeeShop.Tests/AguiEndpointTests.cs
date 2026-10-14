using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Client;
using CoffeeShop.Tests.Fakes;
using CounterService.Common;
using CounterService.Domain;
using CounterService.Features.Orders;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Common;
using DecisionModel.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Tests;

/// <summary>research.md §17 WT1-WT5: the /agui front door through a real AGUIChatClient, DecisionModel and agents faked.</summary>
public sealed class AguiEndpointTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly string[] TwoAsks = ["a pizza", "hmm"];

    private sealed record Turn(List<OrderUiState> Snapshots, List<AIContent> Contents, string Text);

    [Fact]
    public async Task Order_StreamsSnapshotsThenCompletes_AndStoresUnderCustomer()
    {
        using var factory = Build([("place_order", 0.9, 0.95)]);

        var turn = await Run(factory, "a latte", new { customerId = "alice" });

        var last = turn.Snapshots[^1];
        Assert.Equal("Completed", last.Status);
        Assert.Single(last.Lines);
        Assert.Equal(4.50m, last.Total);
        Assert.Equal(1, last.OrderNumber);
        Assert.Equal("place_order", turn.Snapshots.First(s => s.Gate is not null).Gate!.Intent);
        Assert.Equal(2, last.Tickets.Count);
        Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListByCustomer("alice"));
    }

    [Fact]
    public async Task Order_IsStoredWithAuditTrailAndAgUiLog()
    {
        using var factory = Build([("place_order", 0.9, 0.95)]);

        await Run(factory, "a latte", new { customerId = "alice" });

        var trace = Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListByCustomer("alice")).Trace;
        Assert.NotNull(trace);
        Assert.Equal(["a latte"], trace.Conversation);
        Assert.Equal(["message", "provider", "gate", "split", "result"], trace.Audit.Select(a => a.Kind).Distinct().Where(k => k != "ticket"));
        Assert.Contains(trace.Audit, a => a.Kind == "gate" && a.Detail.Contains("place_order", StringComparison.Ordinal));
        Assert.Equal("RUN_STARTED", trace.AgUi[0].Kind);
        Assert.Equal("RUN_FINISHED", trace.AgUi[^1].Kind);
        Assert.Contains(trace.AgUi, e => e.Kind == "STATE_SNAPSHOT" && e.Detail.Contains("order #1", StringComparison.Ordinal));
        Assert.True(trace.AgUi.Zip(trace.AgUi.Skip(1)).All(p => p.First.At <= p.Second.At));
    }

    [Fact]
    public async Task DecisionModelRejectsTheRequest_OrderIsRefusedAndTheReasonIsInTheAuditLog()
    {
        using var factory = Build([], new FailingHandler());

        var turn = await Run(factory, "a latte", new { customerId = "alice" });

        Assert.Equal("Rejected", turn.Snapshots[^1].Status);
        var record = Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListByCustomer("alice"));
        Assert.Contains(record.Trace!.Audit, a => a.Kind == "gate-error" && a.Detail.Contains("400", StringComparison.Ordinal) && a.Detail.Contains("Unknown model", StringComparison.Ordinal));
        Assert.Contains(record.Trace.Audit, a => a.Kind == "result" && a.Detail.Contains("DecisionModelDown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AzureShapedError_IsRefusedAndNamedInTheAuditLog()
    {
        using var factory = Build([], new FailingHandler(System.Net.HttpStatusCode.NotFound,
            """{"error":{"code":"DeploymentNotFound","message":"The API deployment x does not exist."}}"""));

        await Run(factory, "a latte", new { customerId = "alice" });

        var record = Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListByCustomer("alice"));
        Assert.Contains(record.Trace!.Audit, a => a.Kind == "gate-error" && a.Detail.Contains("DeploymentNotFound", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RejectedOrder_AlsoKeepsItsTrail()
    {
        using var factory = Build([("off_topic", 0.9, 0.0)]);

        await Run(factory, "what's the weather?", new { customerId = "alice" });

        var record = Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListByCustomer("alice"));
        Assert.Equal(OrderStatus.Rejected, record.Result.Status);
        Assert.Contains(record.Trace!.Audit, a => a.Kind == "result" && a.Detail.Contains("OffTopic", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unclear_AsksInChat_ThenNextRunCompletes()
    {
        using var factory = Build([("place_order", 0.9, 0.0), ("place_order", 0.9, 0.95)]);

        var first = await Run(factory, "a pizza", new { customerId = "alice" });
        var ask = first.Snapshots[^1];
        Assert.Equal("Asking", ask.Status);
        Assert.Equal(1, ask.Asks);
        Assert.Equal(["a pizza"], ask.History);
        Assert.False(string.IsNullOrWhiteSpace(first.Text));

        var second = await Run(factory, "a latte", new { customerId = "alice", asks = ask.Asks, history = ask.History });
        Assert.Equal("Completed", second.Snapshots[^1].Status);
        Assert.Equal(0, second.Snapshots[^1].Asks);
    }

    [Fact]
    public async Task AskCap_StateWithTwoAsks_RejectsInsteadOfAskingAgain()
    {
        using var factory = Build([("ask_menu", 0.1, 0.0)]);

        var turn = await Run(factory, "hmm", new { customerId = "alice", asks = 2, history = TwoAsks });

        var last = turn.Snapshots[^1];
        Assert.Equal("Rejected", last.Status);
        Assert.Equal(0, last.Asks);
    }

    [Fact]
    public async Task ExtractKeepsFailing_AfterTwoAsks_RejectsAskCapInsteadOfAskingForever()
    {
        // The Gate accepts, the (fake) LLM returns no valid line: before the cap this asked a 3rd, 4th ... question.
        using var factory = Build([("place_order", 0.9, 0.95)], counterJson: """{"lines":[]}""");

        var turn = await Run(factory, "something sweet", new { customerId = "alice", asks = 2, history = TwoAsks });

        Assert.Equal("Rejected", turn.Snapshots[^1].Status);
        Assert.Equal(0, turn.Snapshots[^1].Asks);
    }

    [Fact]
    public async Task MenuRequest_EmitsShowMenuToolResult()
    {
        using var factory = Build([("ask_menu", 0.9, 0.0)]);

        var turn = await Run(factory, "what do you have?", new { customerId = "alice" });

        Assert.Contains(turn.Contents.OfType<FunctionCallContent>(), c => c.Name == CoffeeShopAgent.MenuTool);
        var items = turn.Contents.OfType<FunctionResultContent>().Select(r => ResultJson(r.Result))
            .First(r => r.ValueKind == JsonValueKind.Array);
        Assert.Equal(2, items.GetArrayLength());
    }

    [Fact]
    public async Task HistoryRequest_ShowsCustomersCompletedOrders_OrTemplateWhenNone()
    {
        using var factory = Build([("order_history", 0.9, 0.5), ("order_history", 0.9, 0.5)]);
        var store = factory.Services.GetRequiredService<OrderStore>();
        store.Add(new OrderResult { Status = OrderStatus.Completed, Message = "ok", Total = 4.5m }, "alice");

        var mine = await Run(factory, "what did I order before?", new { customerId = "alice" });
        Assert.Contains(mine.Contents.OfType<FunctionCallContent>(), c => c.Name == CoffeeShopAgent.HistoryTool);

        var newbie = await Run(factory, "what did I order before?", new { customerId = "newbie" });
        Assert.DoesNotContain(newbie.Contents.OfType<FunctionCallContent>(), c => c.Name == CoffeeShopAgent.HistoryTool);
        Assert.Contains("haven't ordered anything yet", newbie.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostileClientState_IsClamped_NotTrusted()
    {
        using var factory = Build([("place_order", 0.9, 0.95)]);

        var turn = await Run(factory, "a latte", new { customerId = "a b;<x>", asks = 99, history = new[] { new string('x', 600), "ok" } });

        Assert.Equal("Completed", turn.Snapshots[^1].Status);
        var record = Assert.Single(factory.Services.GetRequiredService<OrderStore>().ListNewestFirst());
        Assert.Null(record.CustomerId);
    }

    [Fact]
    public async Task OversizeMessage_IsRefusedWithoutRunningTheWorkflow()
    {
        using var factory = Build([("place_order", 0.9, 0.95)]);

        var turn = await Run(factory, new string('x', 501), new { customerId = "alice" });

        Assert.Empty(turn.Snapshots);
        Assert.Contains("between 1 and 500", turn.Text, StringComparison.Ordinal);
        Assert.Empty(factory.Services.GetRequiredService<OrderStore>().ListNewestFirst());
    }

    private static JsonElement ResultJson(object? result) =>
        result as JsonElement? ?? JsonSerializer.Deserialize<JsonElement>(result?.ToString() ?? "null");

    private static async Task<Turn> Run(WebApplicationFactory<Program> factory, string text, object state)
    {
        using var http = factory.CreateClient();
        using var chat = new AGUIChatClient(new AGUIChatClientOptions(http, "/agui"));
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new RunAgentInput
            {
                ThreadId = Guid.NewGuid().ToString("N"),
                State = JsonSerializer.SerializeToElement(state, Web),
            },
        };

        var snapshots = new List<OrderUiState>();
        var contents = new List<AIContent>();
        var textBuilder = new System.Text.StringBuilder();
        await foreach (var update in chat.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, text)], options))
        {
            if (update.RawRepresentation is StateSnapshotEvent snapshot)
            {
                snapshots.Add(snapshot.Snapshot.Deserialize<OrderUiState>(Web)!);
            }

            contents.AddRange(update.Contents);
            textBuilder.Append(update.Text);
        }

        return new Turn(snapshots, contents, textBuilder.ToString());
    }

    private static WebApplicationFactory<Program> Build(IEnumerable<(string IntentChoice, double IntentConfidence, double OnMenuNoul)> gateScript, HttpMessageHandler? handler = null, string counterJson = """{"lines":[{"name":"LATTE","qty":1}]}""") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(new DecisionModelClient(
                new HttpClient(handler ?? new ScriptedDecisionModelHandler(gateScript)) { BaseAddress = new Uri("http://openjev.local/") },
                new DecisionModelOptions()));
            services.AddSingleton<ICatalogClient>(new FakeCatalog());
            services.AddKeyedSingleton(AgentKeys.Counter, CounterAgentFactory.Create(
                FakeChatClient.Returning(counterJson), enableSensitiveData: false));
            services.AddKeyedSingleton(AgentKeys.Barista, BaristaAgentFactory.Create(
                FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":1}]}"""), enableSensitiveData: false));
            services.AddKeyedSingleton(AgentKeys.Kitchen, KitchenAgentFactory.Create(
                FakeChatClient.Returning("""{"items":[]}"""), enableSensitiveData: false));
        }));

    private sealed class FailingHandler(
        System.Net.HttpStatusCode status = System.Net.HttpStatusCode.BadRequest,
        string body = """{"detail":{"error_type":"api_usage_error","message":"Unknown model: english"}}""") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class FakeCatalog : ICatalogClient
    {
        private static readonly IReadOnlyList<MenuItem> Menu =
        [
            new("LATTE", "Latte", 4.50m, Station.Barista),
            new("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
        ];

        public Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default) => Task.FromResult(Menu);
    }
}
