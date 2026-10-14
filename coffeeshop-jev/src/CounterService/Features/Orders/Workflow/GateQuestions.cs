using CounterService.Domain;
using DecisionModel.Client;

namespace CounterService.Features.Orders.Workflow;

/// <summary>The Gate's DecisionModel call (research.md §4.1, intent-routing).</summary>
public static class GateQuestions
{
    public const string QIntent = "intent";
    public const string QOnMenu = "on_menu";

    /// <summary>
    /// The Gate's <c>state</c>. Neutral key names on purpose: a key called <c>history</c> pulled laya's
    /// <c>intent</c> to <c>order_history</c> for every message (measured), so nothing here may echo an answer label.
    /// </summary>
    public static object State(IReadOnlyList<string> previousMessages, string latest) =>
        new { previous_messages = previousMessages, latest_message = latest };

    public const string QOrders = "orders";
    public const string QMenu = "menu";
    public const string QPast = "past";
    public const string QOffTopic = "offtopic";

    /// <summary>State of the noul gate: the latest message only. With earlier messages in the state, laya read "a muffin please" after a question as a question about past orders.</summary>
    public static object NoulState(string latest) => new { customer_message = latest };

    /// <summary>The four yes/no questions (+ on_menu for the ticket) that replace the 5-way choice for <see cref="GateMode.Noul"/> providers.</summary>
    public static IReadOnlyDictionary<string, DecisionModelQuestion> BuildNoul(IReadOnlyList<MenuItem> menu) =>
        new Dictionary<string, DecisionModelQuestion>
        {
            [QOrders] = DecisionModelQuestion.Noul("Does the message ask for specific food or drinks to be made and served, like a coffee or a pastry?"),
            [QMenu] = DecisionModelQuestion.Noul("Does the message ask what food or drinks are available, or ask for the menu or prices?"),
            [QPast] = DecisionModelQuestion.Noul("Does the message ask about orders the customer made earlier?"),
            [QOffTopic] = DecisionModelQuestion.Noul("Is the message about something other than ordering food or drinks, like the weather, news or a joke?"),
            [QOnMenu] = DecisionModelQuestion.Noul($"Does the message only mention items from this list: {string.Join(", ", menu.Select(m => m.DisplayName))}?"),
        };

    public static IReadOnlyDictionary<string, DecisionModelQuestion> Build(IReadOnlyList<string> history, string latest, IReadOnlyList<MenuItem> menu)
    {
        var menuNames = string.Join(", ", menu.Select(m => m.DisplayName));

        return new Dictionary<string, DecisionModelQuestion>
        {
            [QIntent] = DecisionModel.Client.DecisionModelQuestion.Choice(
                "What does the customer want?",
                new Dictionary<string, string?>
                {
                    [IntentPolicy.IntentPlaceOrder] = "Names one or more specific food/drink items they want, with or without an order verb (e.g. 'a latte', 'I'll have a latte', '2 muffins')",
                    [IntentPolicy.IntentAskMenu] = "Asks what's available, for the full menu, or for prices in general - without naming a specific item they want to order",
                    [IntentPolicy.IntentOrderHistory] = "Asks about their own earlier or past orders (e.g. 'what did I order before?', 'show my past orders')",
                    [IntentPolicy.IntentOffTopic] = "Not about ordering food or drinks",
                    ["other"] = "None of the above",
                }),
            [QOnMenu] = DecisionModel.Client.DecisionModelQuestion.Noul(
                $"Is every item in the customer's latest request on the menu ({menuNames})? History for context: {string.Join(" | ", history)}"),
        };
    }
}
