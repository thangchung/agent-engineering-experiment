using CounterService.Domain;
using Jev.Client;

namespace CounterService.Features.Orders.Workflow;

/// <summary>The Gate's Jev call (research.md §4.1, intent-routing).</summary>
public static class GateQuestions
{
    public const string QIntent = "intent";
    public const string QOnMenu = "on_menu";

    public static IReadOnlyDictionary<string, JevQuestion> Build(IReadOnlyList<string> history, string latest, IReadOnlyList<MenuItem> menu)
    {
        var menuNames = string.Join(", ", menu.Select(m => m.DisplayName));

        return new Dictionary<string, JevQuestion>
        {
            [QIntent] = Jev.Client.JevQuestion.Choice(
                "What does the customer want?",
                new Dictionary<string, string?>
                {
                    [IntentPolicy.IntentPlaceOrder] = "Names one or more specific food/drink items they want, with or without an order verb (e.g. 'a latte', 'I'll have a latte', '2 muffins')",
                    [IntentPolicy.IntentAskMenu] = "Asks what's available, for the full menu, or for prices in general - without naming a specific item they want to order",
                    [IntentPolicy.IntentOffTopic] = "Not about ordering food or drinks",
                    ["other"] = "None of the above",
                }),
            [QOnMenu] = Jev.Client.JevQuestion.Noul(
                $"Is every item in the customer's latest request on the menu ({menuNames})? History for context: {string.Join(" | ", history)}"),
        };
    }
}
