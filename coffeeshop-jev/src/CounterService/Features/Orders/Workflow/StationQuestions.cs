using CounterService.Domain;
using Jev.Client;

namespace CounterService.Features.Orders.Workflow;

/// <summary>The Split's Jev call (research.md §4.2, confidence-routing): one <c>station_i</c>
/// choice per line, all lines in a single Jev request.</summary>
public static class StationQuestions
{
    public static string QuestionId(int index) => $"station_{index}";

    public static IReadOnlyDictionary<string, JevQuestion> Build(IReadOnlyList<OrderLine> lines)
    {
        var questions = new Dictionary<string, JevQuestion>();

        for (var i = 0; i < lines.Count; i++)
        {
            questions[QuestionId(i)] = JevQuestion.Choice(
                $"Which station prepares '{lines[i].Name}'?",
                new Dictionary<string, string?>
                {
                    [StationPolicy.ChoiceBarista] = "Coffee and espresso drinks",
                    ["kitchen"] = "Food: pastries, cakes, meals",
                    ["other"] = "Anything else",
                });
        }

        return questions;
    }
}
