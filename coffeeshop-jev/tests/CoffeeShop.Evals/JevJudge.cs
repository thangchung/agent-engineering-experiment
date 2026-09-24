using System.Text.Json;
using Jev.Client;
using Microsoft.Extensions.AI;

namespace CoffeeShop.Evals;

public enum RubricKind
{
    /// <summary>P(true) in [0,1]; pass when >= MinPass.</summary>
    Noul,

    /// <summary>An ordered level, 0-indexed, can be fractional; pass when >= MinPass.</summary>
    Score,
}

/// <summary>One golden rubric item (research.md §10.5).</summary>
public sealed record RubricItem(string Id, string Question, RubricKind Kind, double MinPass, IReadOnlyList<string>? ScoreLevels = null);

/// <summary>One thing to judge: what was asked and what came back.</summary>
public sealed record JudgeItem(string Id, string Query, string Response);

public enum VerdictSource
{
    /// <summary>Jev answered with confidence >= <see cref="JevJudge.AcceptThreshold"/>: accepted as-is.</summary>
    Jev,

    /// <summary>Jev was unsure; an LLM judge (Foundry/OpenAI chat client) answered instead.</summary>
    EscalatedLlm,

    /// <summary>Jev was unsure AND the escalated LLM judge was also unsure/unparseable: needs a human.</summary>
    HumanReview,
}

/// <summary>One rubric item's verdict on one judge item, tagged with which tier produced it
/// (research.md §10.6a: the accept/escalate cascade).</summary>
public sealed record JudgeVerdict(string ItemId, string RubricId, bool? Pass, double Value, double Confidence, VerdictSource Source);

/// <summary>
/// tasks.md T37 (research.md §10.6): Jev as a cheap rubric judge, with the accept/escalate
/// cascade from §10.6a (danielgshea/jev-as-a-judge, Langfuse's decision-model evaluator, and
/// the CMU "JEV-as-a-Judge" paper - all cited there). Jev answers every rubric question about an
/// item in one call; a low-confidence answer escalates to a real LLM judge instead of being
/// trusted or silently dropped.
/// </summary>
public sealed class JevJudge(JevClient jev, IChatClient? escalationJudge)
{
    /// <summary>Same band as research.md §4.2 (StationPolicy.ReviewThreshold): below this, Jev's
    /// own answer isn't trusted on its own.</summary>
    public const double AcceptThreshold = 0.6;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<List<JudgeVerdict>> EvaluateAsync(JudgeItem item, IReadOnlyList<RubricItem> rubric, CancellationToken cancellationToken = default)
    {
        var state = new { item.Query, item.Response };
        var questions = rubric.ToDictionary(r => r.Id, BuildQuestion);
        var response = await jev.AskAsync(state, questions, cancellationToken).ConfigureAwait(false);

        var verdicts = new List<JudgeVerdict>();
        foreach (var r in rubric)
        {
            var answer = response.Answers[r.Id];
            var (value, confidence) = ExtractValueAndConfidence(r, answer);

            if (confidence >= AcceptThreshold)
            {
                verdicts.Add(new JudgeVerdict(item.Id, r.Id, value >= r.MinPass, value, confidence, VerdictSource.Jev));
            }
            else if (escalationJudge is not null)
            {
                verdicts.Add(await EscalateAsync(item, r, confidence, cancellationToken).ConfigureAwait(false));
            }
            else
            {
                verdicts.Add(new JudgeVerdict(item.Id, r.Id, null, value, confidence, VerdictSource.HumanReview));
            }
        }

        return verdicts;
    }

    // Jev reads `state` (query/response/expected, built in EvaluateAsync) and `questions`
    // together in the same request (research.md §2/§5.5) - the instructions text is the
    // rubric question itself, not a copy of the state; Jev correlates them server-side, the
    // same way GateQuestions/StationQuestions never re-embed the order text into their
    // instructions either.
    private static JevQuestion BuildQuestion(RubricItem r) => r.Kind switch
    {
        RubricKind.Noul => JevQuestion.Noul(r.Question),
        RubricKind.Score => JevQuestion.Score(r.Question, r.ScoreLevels!),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    /// <summary>Noul has no Confidence field (research.md §2), unlike Choice/Score - a distance-
    /// from-0.5 proxy stands in for it (0 at noul=0.5, the most uncertain point; 1 at noul=0 or
    /// 1). Naive on purpose: it is a band, not a probability, and the same band (0.6) already
    /// governs Score/Choice confidence, so it keeps one cascade rule instead of two.</summary>
    private static (double Value, double Confidence) ExtractValueAndConfidence(RubricItem r, JevAnswer answer) => r.Kind switch
    {
        RubricKind.Noul => (answer.Noul!.Value, Math.Abs(answer.Noul!.Value - 0.5) * 2),
        RubricKind.Score => (answer.Score!.Value, answer.Confidence!.Value),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    private async Task<JudgeVerdict> EscalateAsync(JudgeItem item, RubricItem r, double jevConfidence, CancellationToken cancellationToken)
    {
        var prompt = r.Kind == RubricKind.Noul
            ? $$"""
               {{r.Question}}

               Query: {{item.Query}}
               Response: {{item.Response}}

               Reply with ONLY a JSON object: {"pass": true|false, "confidence": <0..1>}
               """
            : $$"""
               {{r.Question}}

               Query: {{item.Query}}
               Response: {{item.Response}}
               Levels (0-indexed, lowest first): {{string.Join(", ", r.ScoreLevels!.Select((l, i) => $"{i}={l}"))}}

               Reply with ONLY a JSON object: {"level": <int>, "confidence": <0..1>}
               """;

        try
        {
            var chatResponse = await escalationJudge!.GetResponseAsync(prompt, cancellationToken: cancellationToken).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<EscalationDto>(chatResponse.Text, Json);

            if (dto is null)
            {
                return new JudgeVerdict(item.Id, r.Id, null, 0, jevConfidence, VerdictSource.HumanReview);
            }

            var value = r.Kind == RubricKind.Noul ? (dto.Pass == true ? 1.0 : 0.0) : dto.Level ?? 0;
            var pass = r.Kind == RubricKind.Noul ? dto.Pass : dto.Level >= r.MinPass;

            // The escalated judge is also unsure: neither tier was confident, so a human looks
            // (research.md §10.6a: the cascade's 3rd tier, not just "human OR escalate").
            if (dto.Confidence is null || dto.Confidence < AcceptThreshold)
            {
                return new JudgeVerdict(item.Id, r.Id, pass, value, dto.Confidence ?? 0, VerdictSource.HumanReview);
            }

            return new JudgeVerdict(item.Id, r.Id, pass, value, dto.Confidence.Value, VerdictSource.EscalatedLlm);
        }
        catch (JsonException)
        {
            return new JudgeVerdict(item.Id, r.Id, null, 0, jevConfidence, VerdictSource.HumanReview);
        }
    }

    private sealed record EscalationDto(bool? Pass, int? Level, double? Confidence);
}
