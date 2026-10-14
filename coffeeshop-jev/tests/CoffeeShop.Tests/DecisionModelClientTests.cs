using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DecisionModel.Client;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T03: DecisionModel.Client SDK, all offline against a stub HttpMessageHandler.</summary>
public class DecisionModelClientTests
{
    [Fact]
    public async Task AskAsync_OneOfEachPrimitive_SendsExactRequestShape()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;
        var handler = new StubHandler(async request =>
        {
            captured = request;
            capturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync();
            return JsonResponse(HttpStatusCode.OK, """{"model":"jev-1.0","answers":{},"usage":{"input_tokens":1,"output_tokens":1}}""");
        });

        var client = new DecisionModelClient(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new DecisionModelOptions());

        await client.AskAsync(
            state: new { message = "2 lattes and a croissant" },
            questions: new Dictionary<string, DecisionModelQuestion>
            {
                ["intent"] = DecisionModelQuestion.Choice("What does the customer want?", new Dictionary<string, string?>
                {
                    ["place_order"] = "Orders food or drinks",
                    ["off_topic"] = "Not about ordering food or drinks",
                }),
                ["frustration"] = DecisionModelQuestion.Score("How frustrated is the customer?", ["Calm", "Frustrated", "Very angry"]),
                ["on_menu"] = DecisionModelQuestion.Noul("Is every item on the menu?", new Dictionary<string, string>
                {
                    ["true"] = "Yes",
                    ["false"] = "No",
                }),
            });

        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Equal("http://openjev.local/v1/systemone", captured.RequestUri!.ToString());

        var body = JsonNode.Parse(capturedBody!)!.AsObject();
        Assert.Equal("jev-latest", body["model"]!.GetValue<string>());
        Assert.Equal("2 lattes and a croissant", body["state"]!["message"]!.GetValue<string>());

        var questions = body["questions"]!.AsObject();

        var intent = questions["intent"]!.AsObject();
        Assert.Equal("choice", intent["type"]!.GetValue<string>());
        Assert.Equal("What does the customer want?", intent["instructions"]!.GetValue<string>());
        Assert.Equal("Orders food or drinks", intent["criteria"]!["place_order"]!.GetValue<string>());
        Assert.Equal("Not about ordering food or drinks", intent["criteria"]!["off_topic"]!.GetValue<string>());

        var frustration = questions["frustration"]!.AsObject();
        Assert.Equal("score", frustration["type"]!.GetValue<string>());
        var levels = frustration["criteria"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["Calm", "Frustrated", "Very angry"], levels);

        var onMenu = questions["on_menu"]!.AsObject();
        Assert.Equal("noul", onMenu["type"]!.GetValue<string>());
        Assert.Equal("Is every item on the menu?", onMenu["instructions"]!.GetValue<string>());
        Assert.Equal("Yes", onMenu["criteria"]!["true"]!.GetValue<string>());
    }

    [Fact]
    public async Task AskAsync_ChoiceResponse_ParsesProbabilitiesAndConfidence()
    {
        // Verbatim example shape from docs.typesafe.ai/api (research.md §2).
        const string Json = """
            {
              "model": "jev-1.13.0",
              "answers": {
                "department": {
                  "type": "choice",
                  "choice": "billing",
                  "probabilities": {"billing": 0.88, "technical": 0.12, "sales": 0.0},
                  "confidence": 0.81
                }
              },
              "usage": {"input_tokens": 42, "output_tokens": 3}
            }
            """;
        var client = NewClient(HttpStatusCode.OK, Json);

        var response = await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion>
        {
            ["department"] = DecisionModelQuestion.Choice("?", new Dictionary<string, string?> { ["billing"] = null }),
        });

        Assert.Equal("jev-1.13.0", response.Model);
        var answer = response.Answers["department"];
        Assert.Equal("choice", answer.Type);
        Assert.Equal("billing", answer.Choice);
        Assert.Equal(0.88, answer.Probabilities!["billing"]);
        Assert.Equal(0.81, answer.Confidence);
        Assert.Equal(42, response.Usage!.InputTokens);
    }

    [Fact]
    public async Task AskAsync_ScoreResponse_ParsesLegendAndScore()
    {
        const string Json = """
            {
              "model": "jev-1.13.0",
              "answers": {
                "frustration": {
                  "type": "score",
                  "score": 1.05,
                  "legend": {"0": "Calm", "1": "Frustrated", "2": "Very angry"},
                  "probabilities": {"0": 0.0, "1": 0.95, "2": 0.05},
                  "confidence": 0.92
                }
              },
              "usage": {"input_tokens": 10, "output_tokens": 2}
            }
            """;
        var client = NewClient(HttpStatusCode.OK, Json);

        var response = await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion>
        {
            ["frustration"] = DecisionModelQuestion.Score("?", ["a", "b", "c"]),
        });

        var answer = response.Answers["frustration"];
        Assert.Equal("score", answer.Type);
        Assert.Equal(1.05, answer.Score);
        Assert.Equal("Frustrated", answer.Legend!["1"]);
        Assert.Equal(0.92, answer.Confidence);
    }

    [Fact]
    public async Task AskAsync_NoulResponse_HasNoConfidenceField()
    {
        const string Json = """
            {"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":5,"output_tokens":1}}
            """;
        var client = NewClient(HttpStatusCode.OK, Json);

        var response = await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion>
        {
            ["is_urgent"] = DecisionModelQuestion.Noul("?"),
        });

        var answer = response.Answers["is_urgent"];
        Assert.Equal("noul", answer.Type);
        Assert.Equal(0.95, answer.Noul);
        Assert.Null(answer.Confidence);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"detail":"Too many score levels. Must have at most 10 levels."}""", JsonValueKind.String)]
    [InlineData((HttpStatusCode)403, """{"detail":{"error_type":"authentication_error","message":"Must supply an API key!"}}""", JsonValueKind.Object)]
    [InlineData((HttpStatusCode)422, """{"detail":[{"loc":["body","state"],"msg":"field required"}]}""", JsonValueKind.Array)]
    public async Task AskAsync_ErrorResponse_ThrowsDecisionModelExceptionWithCorrectDetailShape(
        HttpStatusCode status, string body, JsonValueKind expectedKind)
    {
        var client = NewClient(status, body);

        var ex = await Assert.ThrowsAsync<DecisionModelException>(() => client.AskAsync("x", new Dictionary<string, DecisionModelQuestion>
        {
            ["q"] = DecisionModelQuestion.Noul("?"),
        }));

        Assert.Equal((int)status, ex.StatusCode);
        Assert.NotNull(ex.Detail);
        Assert.Equal(expectedKind, ex.Detail!.Value.ValueKind);
    }

    [Fact]
    public async Task AskAsync_NoApiKey_SendsNoAuthorizationHeader()
    {
        AuthenticationHeaderValue? seenAuth = null;
        var handler = new StubHandler(request =>
        {
            seenAuth = request.Headers.Authorization;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyResponseJson));
        });
        var client = new DecisionModelClient(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new DecisionModelOptions());

        await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion> { ["q"] = DecisionModelQuestion.Noul("?") });

        Assert.Null(seenAuth);
    }

    [Fact]
    public async Task AskAsync_WithApiKey_SendsBearerAuthorizationHeader()
    {
        AuthenticationHeaderValue? seenAuth = null;
        var handler = new StubHandler(request =>
        {
            seenAuth = request.Headers.Authorization;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, EmptyResponseJson));
        });
        var client = new DecisionModelClient(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new DecisionModelOptions { ApiKey = "secret-key" });

        await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion> { ["q"] = DecisionModelQuestion.Noul("?") });

        Assert.NotNull(seenAuth);
        Assert.Equal("Bearer", seenAuth!.Scheme);
        Assert.Equal("secret-key", seenAuth.Parameter);
    }

    [Fact]
    public async Task AskAsync_529Overloaded_RetriesThenSucceeds_WhenResilienceHandlerIsAttached()
    {
        // AddDecisionModelClient wires the standard resilience handler in production (ServiceCollectionExtensions.cs);
        // here we attach one directly to prove 529 is retried, matching research.md §2 ("Retry 429 and 529").
        var callCount = 0;
        var handler = new StubHandler(_ =>
        {
            callCount++;
            return Task.FromResult(callCount < 3
                ? new HttpResponseMessage((HttpStatusCode)529)
                : JsonResponse(HttpStatusCode.OK, EmptyResponseJson));
        });

        var services = new ServiceCollection();
        services.AddHttpClient<DecisionModelClient>(c => c.BaseAddress = new Uri("http://openjev.local/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler)
            .AddStandardResilienceHandler();
        services.AddSingleton(new DecisionModelOptions());
        // DecisionModelClient needs a DecisionModelOptions instance resolvable by ActivatorUtilities; register it directly.
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<DecisionModelClient>();
        var response = await client.AskAsync("x", new Dictionary<string, DecisionModelQuestion> { ["q"] = DecisionModelQuestion.Noul("?") });

        Assert.True(callCount >= 2, $"expected at least 2 attempts (1 retry), got {callCount}");
        Assert.Equal("jev-1.0", response.Model);
    }

    private const string EmptyResponseJson = """{"model":"jev-1.0","answers":{},"usage":{"input_tokens":0,"output_tokens":0}}""";

    private static async Task<HttpRequestMessage> SendWith(DecisionModelOptions options)
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHandler(request =>
        {
            captured = request;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"model":"m","answers":{},"usage":{"input_tokens":1,"output_tokens":1}}"""));
        });
        var client = new DecisionModelClient(new HttpClient(handler) { BaseAddress = new Uri("https://res.services.ai.azure.com/") }, options);

        await client.AskAsync(new { a = 1 }, new Dictionary<string, DecisionModelQuestion> { ["q"] = DecisionModelQuestion.Noul("Is it?") });
        return captured!;
    }

    [Fact]
    public async Task AskAsync_FoundryStyle_UsesProviderPathAndApiKeyHeader()
    {
        var request = await SendWith(new DecisionModelOptions
        {
            Path = "providers/microsoft/v1/systemone",
            ApiKey = "k-123",
            ApiKeyHeader = "api-key",
        });

        Assert.Equal("https://res.services.ai.azure.com/providers/microsoft/v1/systemone", request.RequestUri!.ToString());
        Assert.Equal("k-123", Assert.Single(request.Headers.GetValues("api-key")));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task AskAsync_TokenProvider_SendsBearerTokenAndBeatsTheApiKey()
    {
        var request = await SendWith(new DecisionModelOptions
        {
            ApiKey = "ignored",
            ApiKeyHeader = "api-key",
            TokenProvider = _ => ValueTask.FromResult("entra-token"),
        });

        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("entra-token", request.Headers.Authorization.Parameter);
        Assert.False(request.Headers.Contains("api-key"));
    }

    private static DecisionModelClient NewClient(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(status, body)));
        return new DecisionModelClient(new HttpClient(handler) { BaseAddress = new Uri("http://openjev.local/") }, new DecisionModelOptions());
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
