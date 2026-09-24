namespace CoffeeShop.Evals;

/// <summary>The golden rubric items that are judge-scored, not deterministic (research.md
/// §10.1 rule: "deterministic checks gate pass/fail, judges only score soft qualities"). These
/// 3 are exactly R-C3, R-T4, R-D4 from research.md §10.5.</summary>
public static class RubricSet
{
    public static readonly RubricItem ClarifyIsPoliteAndClear = new(
        "R-C3",
        "Is this a polite, clear question a barista would ask a customer?",
        RubricKind.Noul,
        MinPass: 0.7);

    public static readonly RubricItem TicketRealism = new(
        "R-T4",
        "How realistic are these prep steps?",
        RubricKind.Score,
        MinPass: 1,
        ScoreLevels: ["unrealistic", "plausible", "realistic"]);

    public static readonly RubricItem DeliverIsFriendlyAndConfirms = new(
        "R-D4",
        "Does the reply sound friendly and clearly confirm the order?",
        RubricKind.Noul,
        MinPass: 0.7);
}
