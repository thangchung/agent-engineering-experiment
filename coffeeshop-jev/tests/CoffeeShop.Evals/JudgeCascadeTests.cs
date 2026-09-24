using System.Text.Json;
using System.Text.Json.Serialization;
using CounterService.Domain;
using CounterService.Features.Orders.Workflow;
using Microsoft.Extensions.AI;

namespace CoffeeShop.Evals;

/// <summary>
/// tasks.md T36 (just enough to feed real judge items - the deterministic R-E/R-C/R-T/R-D
/// checks are out of this pass's scope) + T37: real agent responses, judged by the real
/// <see cref="JevJudge"/> accept/escalate cascade (research.md §10.6a), against real Jev and a
/// real escalation LLM. Every response here comes from the exact same `Prompts.cs` builders and
/// agent factories the app itself uses (research.md §10.4: "an eval of a copied prompt proves
/// nothing about the real one").
/// </summary>
public class JudgeCascadeTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Cascade_OverRealAgentResponses_ProducesVerdictsPerRubricItem()
    {
        EvalConfig.RequireOpenJev();
        EvalConfig.RequireOpenAi();
        var counter = EvalClients.CounterAgent();
        var barista = EvalClients.BaristaAgent();
        var judge = new JevJudge(EvalClients.Jev(), EvalClients.ChatClient());

        var latte = new OrderLine { Name = "LATTE", Qty = 1, Price = 4.50m };

        // R-C3: the clarify question for an off-menu order (ClarifyExecutor's own prompt).
        var clarifyPrompt = ClarifyPrompt.Build("a pizza is not on our menu", EvalMenu.Items);
        var clarifyText = (await counter.RunAsync([new ChatMessage(ChatRole.User, clarifyPrompt)])).Text;

        // R-T4: a barista ticket (same inline payload shape StationExecutor sends).
        var ticketPrompt = JsonSerializer.Serialize(new[] { new { latte.Name, latte.Qty } }, Json);
        var ticketText = (await barista.RunAsync([new ChatMessage(ChatRole.User, ticketPrompt)])).Text;

        // R-D4: the deliver reply (DeliverExecutor's own prompt).
        var deliverPrompt = DeliverPrompt.Build([latte], latte.Price, anyClamped: false);
        var deliverText = (await counter.RunAsync([new ChatMessage(ChatRole.User, deliverPrompt)])).Text;

        var items = new (JudgeItem Item, RubricItem Rubric)[]
        {
            (new JudgeItem("clarify-pizza", clarifyPrompt, clarifyText), RubricSet.ClarifyIsPoliteAndClear),
            (new JudgeItem("ticket-latte", ticketPrompt, ticketText), RubricSet.TicketRealism),
            (new JudgeItem("deliver-latte", deliverPrompt, deliverText), RubricSet.DeliverIsFriendlyAndConfirms),
        };

        var verdicts = new List<JudgeVerdict>();
        foreach (var (item, rubric) in items)
        {
            verdicts.AddRange(await judge.EvaluateAsync(item, [rubric]));
        }

        EvalOutput.WriteJsonl("judge.jsonl", verdicts, Json);

        // Every rubric item must reach a verdict at some tier - Jev, the escalated LLM, or an
        // explicit HumanReview flag. It must never come back as a silent null with no source.
        Assert.All(verdicts, v => Assert.True(v.Pass.HasValue || v.Source == VerdictSource.HumanReview));
    }
}
