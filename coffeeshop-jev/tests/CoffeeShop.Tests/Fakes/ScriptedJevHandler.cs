using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace CoffeeShop.Tests.Fakes;

/// <summary>
/// A stub Jev server for workflow-level tests: it inspects which questions a request asked
/// (the Gate's "intent"/"on_menu" vs the Split's "station_i") and answers from a scripted
/// sequence, so a full <see cref="CounterService.Features.Orders.Workflow.OrderWorkflow"/> run
/// can be driven end to end without a network.
/// </summary>
public sealed class ScriptedJevHandler(
    IEnumerable<(string IntentChoice, double IntentConfidence, double OnMenuNoul)> gateScript,
    string stationChoice = "barista",
    double stationConfidence = 0.95) : HttpMessageHandler
{
    private readonly Queue<(string IntentChoice, double IntentConfidence, double OnMenuNoul)> _gateScript = new(gateScript);

    public int GateCalls { get; private set; }
    public int SplitCalls { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var text = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var body = JsonNode.Parse(text)!.AsObject();
        var questions = body["questions"]!.AsObject();

        string json;
        if (questions.ContainsKey("intent"))
        {
            GateCalls++;
            var (intentChoice, intentConfidence, onMenuNoul) = _gateScript.Count > 0 ? _gateScript.Dequeue() : _gateScript.Last();
            json = new JsonObject
            {
                ["model"] = "jev-test",
                ["answers"] = new JsonObject
                {
                    ["intent"] = new JsonObject
                    {
                        ["type"] = "choice",
                        ["choice"] = intentChoice,
                        ["probabilities"] = new JsonObject(),
                        ["confidence"] = intentConfidence,
                    },
                    ["on_menu"] = new JsonObject { ["type"] = "noul", ["noul"] = onMenuNoul },
                },
                ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
            }.ToJsonString();
        }
        else
        {
            SplitCalls++;
            var answers = new JsonObject();
            var i = 0;
            foreach (var _ in questions)
            {
                answers[$"station_{i}"] = new JsonObject
                {
                    ["type"] = "choice",
                    ["choice"] = stationChoice,
                    ["probabilities"] = new JsonObject(),
                    ["confidence"] = stationConfidence,
                };
                i++;
            }

            json = new JsonObject
            {
                ["model"] = "jev-test",
                ["answers"] = answers,
                ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
            }.ToJsonString();
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }
}
