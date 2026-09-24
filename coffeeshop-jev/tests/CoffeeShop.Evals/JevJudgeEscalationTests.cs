using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using CoffeeShop.Tests.Fakes;
using Jev.Client;

namespace CoffeeShop.Evals;

/// <summary>
/// tasks.md T37, offline half: the live <see cref="JudgeCascadeTests"/> run proved the cascade
/// against the real (usually confident) Jev server, which never actually took the escalate
/// branch. This proves that branch itself, deterministically and without a network - a scripted
/// low-confidence Jev answer, then a scripted low-confidence *and* a scripted confident LLM
/// answer, so both the escalate step and the "still unsure -> HumanReview" 3rd tier are checked
/// on every `dotnet test`, not only when the real server happens to be unsure.
/// </summary>
public class JevJudgeEscalationTests
{
    private static readonly RubricItem Rubric = new("R-TEST", "Is this good?", RubricKind.Noul, MinPass: 0.7);

    [Fact]
    public async Task LowConfidenceJevAnswer_EscalatesToConfidentLlmJudge()
    {
        var jev = NewJev(noul: 0.55, confidence: null); // distance-from-0.5 proxy: |0.55-0.5|*2 = 0.1, well under 0.6
        var llm = FakeChatClient.Returning("""{"pass": true, "confidence": 0.95}""");
        var judge = new JevJudge(jev, llm);

        var verdicts = await judge.EvaluateAsync(new JudgeItem("i1", "q", "r"), [Rubric]);

        var verdict = Assert.Single(verdicts);
        Assert.Equal(VerdictSource.EscalatedLlm, verdict.Source);
        Assert.True(verdict.Pass);
    }

    [Fact]
    public async Task LowConfidenceJevAnswer_AndLowConfidenceLlmAnswer_FlagsHumanReview()
    {
        var jev = NewJev(noul: 0.52, confidence: null);
        var llm = FakeChatClient.Returning("""{"pass": true, "confidence": 0.3}""");
        var judge = new JevJudge(jev, llm);

        var verdicts = await judge.EvaluateAsync(new JudgeItem("i1", "q", "r"), [Rubric]);

        Assert.Equal(VerdictSource.HumanReview, Assert.Single(verdicts).Source);
    }

    [Fact]
    public async Task LowConfidenceJevAnswer_NoEscalationJudgeConfigured_FlagsHumanReviewDirectly()
    {
        var jev = NewJev(noul: 0.51, confidence: null);
        var judge = new JevJudge(jev, escalationJudge: null);

        var verdicts = await judge.EvaluateAsync(new JudgeItem("i1", "q", "r"), [Rubric]);

        var verdict = Assert.Single(verdicts);
        Assert.Equal(VerdictSource.HumanReview, verdict.Source);
        Assert.Null(verdict.Pass);
    }

    [Fact]
    public async Task HighConfidenceJevAnswer_NeverEscalates()
    {
        var jev = NewJev(noul: 0.97, confidence: null);
        var llm = new FakeChatClient((_, _) => throw new InvalidOperationException("must not escalate"));
        var judge = new JevJudge(jev, llm);

        var verdicts = await judge.EvaluateAsync(new JudgeItem("i1", "q", "r"), [Rubric]);

        var verdict = Assert.Single(verdicts);
        Assert.Equal(VerdictSource.Jev, verdict.Source);
        Assert.True(verdict.Pass);
    }

    private static JevClient NewJev(double noul, double? confidence) =>
        new(new HttpClient(new StubJevHandler(noul, confidence)) { BaseAddress = new Uri("http://jev.local/") }, new JevOptions());

    /// <summary>Answers whichever single noul question it's asked with a fixed value - generic
    /// over the rubric id, unlike research's ScriptedJevHandler (which is gate/split-shaped).</summary>
    private sealed class StubJevHandler(double noul, double? confidence) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var text = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var body = JsonNode.Parse(text)!.AsObject();
            var questions = body["questions"]!.AsObject();
            var answers = new JsonObject();

            foreach (var (id, _) in questions)
            {
                answers[id] = new JsonObject
                {
                    ["type"] = "noul",
                    ["noul"] = noul,
                    ["confidence"] = confidence is null ? null : JsonValue.Create(confidence.Value),
                };
            }

            var json = new JsonObject
            {
                ["model"] = "jev-test",
                ["answers"] = answers,
                ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 1 },
            }.ToJsonString();

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
