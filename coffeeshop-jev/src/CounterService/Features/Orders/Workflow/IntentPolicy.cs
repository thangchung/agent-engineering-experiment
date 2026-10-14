using CounterService.Domain;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Pure decision function for the Gate step (research.md §4.1, DecisionModel intent-routing). Takes
/// DecisionModel's raw answer plus how many clarify turns have already happened, and decides
/// Accepted / Unclear / Rejected. No I/O, no DecisionModel call - <see cref="GateExecutor"/> (T09) does that
/// and hands the answer to this function.
/// </summary>
public static class IntentPolicy
{
    /// <summary>docs.typesafe.ai/patterns/intent-routing: "if intent.confidence &lt; 0.5 -&gt; human".</summary>
    public const double IntentConfidenceThreshold = 0.5;

    /// <summary>research.md §4.1: the cut on P(on_menu) - noul has no confidence field.</summary>
    public const double OnMenuThreshold = 0.5;

    /// <summary>research.md §4.1: after this many unresolved asks, give up and reject.</summary>
    public const int MaxAsks = 2;

    /// <summary>Choice values from the Gate's <c>intent</c> question (research.md §9 probe).</summary>
    public const string IntentPlaceOrder = "place_order";
    public const string IntentAskMenu = "ask_menu";
    public const string IntentOffTopic = "off_topic";
    public const string IntentOrderHistory = "order_history";

    /// <summary>Noul-gate cut-offs (laya, probed 2026-10-14): history 0.85-0.89 vs 0.71 for a cart message, menu 0.62-0.72, orders 0.53-0.81 (cart text "2 × Latte, 1 × Muffin" scored 0.60) vs at most 0.27 for non-orders.</summary>
    public const double NoulHistory = 0.8;
    public const double NoulMenu = 0.6;
    public const double NoulOrder = 0.45;
    public const double NoulOffTopic = 0.6;

    /// <summary>
    /// Maps the four yes/no scores of <see cref="GateMode.Noul"/> to the (choice, confidence) pair <see cref="Decide"/> takes.
    /// Order matters: past orders first (strict), then menu when it beats orders, then orders, then off-topic; anything else is
    /// "other" with confidence 0, which <see cref="Decide"/> turns into a question.
    /// </summary>
    public static (string Choice, double Confidence) FromNouls(double orders, double menu, double past, double offTopic)
    {
        if (past >= NoulHistory)
        {
            return (IntentOrderHistory, past);
        }

        if (menu >= NoulMenu && menu > orders)
        {
            return (IntentAskMenu, menu);
        }

        if (orders >= NoulOrder)
        {
            return (IntentPlaceOrder, orders);
        }

        return offTopic >= NoulOffTopic ? (IntentOffTopic, offTopic) : ("other", 0);
    }

    /// <summary>
    /// Decide what to do next after one Gate call to DecisionModel.
    /// </summary>
    /// <param name="intentChoice">The <c>intent</c> question's <c>choice</c> answer.</param>
    /// <param name="intentConfidence">The <c>intent</c> question's <c>confidence</c> answer.</param>
    /// <param name="onMenuNoul">The <c>on_menu</c> question's <c>noul</c> answer (P(true)).</param>
    /// <param name="asks">How many clarify questions have already been asked this order.</param>
    /// <param name="profile">Provider calibration; null = the default (TypeSafe) cut-offs.</param>
    /// <returns>An <see cref="Accepted"/>, <see cref="Unclear"/>, or <see cref="Rejected"/> message.</returns>
    public static object Decide(string intentChoice, double intentConfidence, double onMenuNoul, int asks, DecisionProfile? profile = null)
    {
        var minConfidence = profile?.IntentConfidence ?? IntentConfidenceThreshold;
        var minOnMenu = profile?.OnMenu ?? OnMenuThreshold;

        if (intentConfidence < minConfidence)
        {
            return UnclearOrGiveUp("Sorry, I didn't quite catch that - what would you like to order?", asks);
        }

        return intentChoice switch
        {
            IntentPlaceOrder when onMenuNoul >= minOnMenu => new Accepted(),
            IntentPlaceOrder => UnclearOrGiveUp("One or more of those items aren't on our menu - could you choose from the menu?", asks),
            IntentAskMenu => new MenuRequested(),
            IntentOrderHistory => new HistoryRequested(),
            _ => new Rejected(RejectReason.OffTopic, "Sorry, I can only help with food and drink orders."),
        };
    }

    private static object UnclearOrGiveUp(string question, int asks) =>
        asks >= MaxAsks
            ? new Rejected(RejectReason.AskCap, "I'm sorry, I still couldn't understand your order. Please try again.")
            : new Unclear(question);
}
