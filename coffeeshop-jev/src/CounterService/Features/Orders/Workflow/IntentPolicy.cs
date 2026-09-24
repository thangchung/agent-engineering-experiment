using CounterService.Domain;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Pure decision function for the Gate step (research.md §4.1, Jev intent-routing). Takes
/// Jev's raw answer plus how many clarify turns have already happened, and decides
/// Accepted / Unclear / Rejected. No I/O, no Jev call - <see cref="GateExecutor"/> (T09) does that
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

    /// <summary>
    /// Decide what to do next after one Gate call to Jev.
    /// </summary>
    /// <param name="intentChoice">The <c>intent</c> question's <c>choice</c> answer.</param>
    /// <param name="intentConfidence">The <c>intent</c> question's <c>confidence</c> answer.</param>
    /// <param name="onMenuNoul">The <c>on_menu</c> question's <c>noul</c> answer (P(true)).</param>
    /// <param name="asks">How many clarify questions have already been asked this order.</param>
    /// <returns>An <see cref="Accepted"/>, <see cref="Unclear"/>, or <see cref="Rejected"/> message.</returns>
    public static object Decide(string intentChoice, double intentConfidence, double onMenuNoul, int asks)
    {
        if (intentConfidence < IntentConfidenceThreshold)
        {
            return UnclearOrGiveUp("Sorry, I didn't quite catch that - what would you like to order?", asks);
        }

        return intentChoice switch
        {
            IntentPlaceOrder when onMenuNoul >= OnMenuThreshold => new Accepted(),
            IntentPlaceOrder => UnclearOrGiveUp("One or more of those items aren't on our menu - could you choose from the menu?", asks),
            IntentAskMenu => new MenuRequested(),
            _ => new Rejected(RejectReason.OffTopic, "Sorry, I can only help with food and drink orders."),
        };
    }

    private static object UnclearOrGiveUp(string question, int asks) =>
        asks >= MaxAsks
            ? new Rejected(RejectReason.AskCap, "I'm sorry, I still couldn't understand your order. Please try again.")
            : new Unclear(question);
}
