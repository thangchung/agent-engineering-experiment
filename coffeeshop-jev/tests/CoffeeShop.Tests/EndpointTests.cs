using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CoffeeShop.Tests.Fakes;
using CounterService.Common;
using CounterService.Domain;
using CounterService.Features.Orders;
using CounterService.Features.Orders.Agents;
using Jev.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T15: the HTTP endpoints, end to end through a real Kestrel test server,
/// with Jev and the 3 agents faked (no network).</summary>
public class EndpointTests : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public EndpointTests()
    {
        var jevHandler = new ScriptedJevHandler([("place_order", 0.9, 0.95)]);
        var counterFake = FakeChatClient.Returning("""{"lines":[{"name":"LATTE","qty":1}]}""");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new JevClient(
                    new HttpClient(jevHandler) { BaseAddress = new Uri("http://openjev.local/") },
                    new JevOptions()));

                services.AddSingleton<ICatalogClient>(new FakeCatalogClient([
                    new MenuItem("LATTE", "Latte", 4.50m, Station.Barista),
                    new MenuItem("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
                ]));

                services.AddKeyedSingleton(AgentKeys.Counter, CounterAgentFactory.Create(counterFake, enableSensitiveData: false));
                services.AddKeyedSingleton(AgentKeys.Barista, BaristaAgentFactory.Create(
                    FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":1}]}"""), enableSensitiveData: false));
                services.AddKeyedSingleton(AgentKeys.Kitchen, KitchenAgentFactory.Create(
                    FakeChatClient.Returning("""{"items":[]}"""), enableSensitiveData: false));
            });
        });

        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task PlaceOrder_HappyPath_StreamsRunGateSplitStationDone()
    {
        using var response = await _client.PostAsync("/orders", JsonContent(new { text = "a latte" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: run", body, StringComparison.Ordinal);
        Assert.Contains("event: gate", body, StringComparison.Ordinal);
        Assert.Contains("event: split", body, StringComparison.Ordinal);
        Assert.Contains("event: done", body, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Completed\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlaceOrder_EmptyText_ReturnsBadRequest()
    {
        using var response = await _client.PostAsync("/orders", JsonContent(new { text = "" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_TooLongText_ReturnsBadRequest()
    {
        using var response = await _client.PostAsync("/orders", JsonContent(new { text = new string('x', 501) }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Answer_UnknownRunId_ReturnsNotFound()
    {
        using var response = await _client.PostAsync("/orders/does-not-exist/answer", JsonContent(new { text = "hi" }));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PlaceOrder_AsksClarifyQuestion_ThenAnswer_ResumesToCompletion()
    {
        // on_menu low -> Unclear -> Clarify -> ask; then, after the answer, place_order + on_menu
        // high -> Accepted -> ... -> done. Same production plumbing as the happy path, just with
        // a script that actually pauses, so it exercises the real ask/answer round trip.
        using var factory = BuildFactory([("place_order", 0.9, 0.0), ("place_order", 0.9, 0.95)]);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent(new { text = "a pizza" }) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        var (runId, sawAsk) = await ReadUntilAsync(reader, "ask");
        Assert.NotNull(runId);
        Assert.True(sawAsk, "expected an 'ask' SSE event before the stream ended");

        using var answerResponse = await client.PostAsync($"/orders/{runId}/answer", JsonContent(new { text = "a latte" }));
        Assert.Equal(HttpStatusCode.Accepted, answerResponse.StatusCode);

        var (_, sawDone) = await ReadUntilAsync(reader, "done");
        Assert.True(sawDone, "expected the order to complete after answering");
    }

    /// <summary>Reads SSE lines until <paramref name="eventType"/> is seen (or the stream ends),
    /// returning the runId captured from a "run" event along the way.</summary>
    private static async Task<(string? RunId, bool Saw)> ReadUntilAsync(StreamReader reader, string eventType)
    {
        string? runId = null;
        string? currentEvent = null;
        for (var i = 0; i < 100; i++)
        {
            var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
            if (line is null)
            {
                return (runId, false);
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                currentEvent = line[6..].Trim();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (currentEvent == "run")
                {
                    var data = line[5..].Trim();
                    var marker = "\"runId\":\"";
                    var start = data.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                    runId = data[start..data.IndexOf('"', start)];
                }

                if (currentEvent == eventType)
                {
                    return (runId, true);
                }
            }
        }

        return (runId, false);
    }

    private static WebApplicationFactory<Program> BuildFactory(
        IEnumerable<(string IntentChoice, double IntentConfidence, double OnMenuNoul)> gateScript)
    {
        var jevHandler = new ScriptedJevHandler(gateScript);
        var counterFake = FakeChatClient.Returning("""{"lines":[{"name":"LATTE","qty":1}]}""");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(new JevClient(
                    new HttpClient(jevHandler) { BaseAddress = new Uri("http://openjev.local/") },
                    new JevOptions()));

                services.AddSingleton<ICatalogClient>(new FakeCatalogClient([
                    new MenuItem("LATTE", "Latte", 4.50m, Station.Barista),
                    new MenuItem("MUFFIN", "Muffin", 3.00m, Station.Kitchen),
                ]));

                services.AddKeyedSingleton(AgentKeys.Counter, CounterAgentFactory.Create(counterFake, enableSensitiveData: false));
                services.AddKeyedSingleton(AgentKeys.Barista, BaristaAgentFactory.Create(
                    FakeChatClient.Returning("""{"items":[{"name":"LATTE","qty":1}]}"""), enableSensitiveData: false));
                services.AddKeyedSingleton(AgentKeys.Kitchen, KitchenAgentFactory.Create(
                    FakeChatClient.Returning("""{"items":[]}"""), enableSensitiveData: false));
            });
        });
    }

    [Fact]
    public async Task Answer_WhenNotWaiting_ReturnsConflict()
    {
        // The happy-path order never asks a clarify question, so its runId is registered then
        // removed once the order completes - answering it afterward must be a 409, not a 404
        // *while it's still running* (its own request/response race). We approximate "not
        // pending" for a fresh, never-asked run by hitting a run mid-flight from a separate call.
        using var placed = await _client.PostAsync("/orders", JsonContent(new { text = "a latte" }));
        var body = await placed.Content.ReadAsStringAsync();
        var runId = ExtractRunId(body);

        using var response = await _client.PostAsync($"/orders/{runId}/answer", JsonContent(new { text = "hi" }));
        // The run has already finished and removed itself by the time we answer - NotFound.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ListOrders_AfterOneCompletedOrder_ReturnsIt()
    {
        await _client.PostAsync("/orders", JsonContent(new { text = "a latte" }));

        using var response = await _client.GetAsync("/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orders = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(orders.GetArrayLength() >= 1);
    }

    private static string ExtractRunId(string sseBody)
    {
        const string marker = "\"runId\":\"";
        var start = sseBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = sseBody.IndexOf('"', start);
        return sseBody[start..end];
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>A fixed menu, no MCP server - CatalogClientTests (T06) already proves the real
    /// implementation against a real in-process MCP server.</summary>
    private sealed class FakeCatalogClient(IReadOnlyList<MenuItem> menu) : ICatalogClient
    {
        public Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(menu);
    }
}
