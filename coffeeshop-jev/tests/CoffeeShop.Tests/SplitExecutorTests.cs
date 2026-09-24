using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CoffeeShop.Tests.Fakes;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;
using Jev.Client;

namespace CoffeeShop.Tests;

/// <summary>tasks.md T11: SplitExecutor, offline against a stub Jev HttpMessageHandler.</summary>
public class SplitExecutorTests
{
    [Fact]
    public async Task HandleAsync_TwoLines_SendsOneJevCall_WithBothStationQuestions()
    {
        JsonNode? capturedBody = null;
        var jev = NewJevClient(async request =>
        {
            var text = await request.Content!.ReadAsStringAsync();
            capturedBody = JsonNode.Parse(text);
            return JsonHttpResponse(CannedStationResponse(("barista", 0.95), ("kitchen", 0.7)));
        });

        var draft = new OrderDraft([Line("LATTE"), Line("MUFFIN")]);
        await new SplitExecutor(jev).HandleAsync(draft, new NoopWorkflowContext());

        var questions = capturedBody!["questions"]!.AsObject();
        Assert.True(questions.ContainsKey("station_0"));
        Assert.True(questions.ContainsKey("station_1"));
        Assert.Equal(2, questions.Count);
    }

    [Fact]
    public async Task HandleAsync_HighAndMidConfidence_MapToExpectedStationsAndFlags()
    {
        var jev = NewJevClient(CannedStationResponse(("barista", 0.95), ("kitchen", 0.7)));
        var draft = new OrderDraft([Line("LATTE"), Line("MUFFIN")]);

        var split = await new SplitExecutor(jev).HandleAsync(draft, new NoopWorkflowContext());

        Assert.Equal(Station.Barista, split.Lines[0].Station);
        Assert.Equal(Flag.None, split.Lines[0].Flag);
        Assert.Equal(Station.Kitchen, split.Lines[1].Station);
        Assert.Equal(Flag.Confirm, split.Lines[1].Flag);
    }

    [Fact]
    public async Task HandleAsync_JevDown_EveryLineGoesToKitchenReview_SplitOrderStillSent()
    {
        var jev = NewJevClient(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"detail":"boom"}""", Encoding.UTF8, "application/json"),
        }));

        var draft = new OrderDraft([Line("LATTE"), Line("MUFFIN")]);
        var split = await new SplitExecutor(jev).HandleAsync(draft, new NoopWorkflowContext());

        Assert.All(split.Lines, l =>
        {
            Assert.Equal(Station.Kitchen, l.Station);
            Assert.Equal(Flag.Review, l.Flag);
        });
    }

    private static OrderLine Line(string name) => new() { Name = name, Qty = 1, Price = 1.0m };

    private static string CannedStationResponse(params (string Choice, double Confidence)[] answers)
    {
        var body = new JsonObject { ["model"] = "jev-test" };
        var answersNode = new JsonObject();
        for (var i = 0; i < answers.Length; i++)
        {
            answersNode[$"station_{i}"] = new JsonObject
            {
                ["type"] = "choice",
                ["choice"] = answers[i].Choice,
                ["probabilities"] = new JsonObject(),
                ["confidence"] = answers[i].Confidence,
            };
        }

        body["answers"] = answersNode;
        body["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 };
        return body.ToJsonString();
    }

    private static JevClient NewJevClient(string cannedJson) =>
        NewJevClient(_ => Task.FromResult(JsonHttpResponse(cannedJson)));

    private static JevClient NewJevClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://openjev.local/") }, new JevOptions());

    private static HttpResponseMessage JsonHttpResponse(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
