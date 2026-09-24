namespace CounterService.Domain;

/// <summary>Which side of the counter prepares a line. research.md §4.2 / §15.2.</summary>
public enum Station
{
    Barista,
    Kitchen,
}

/// <summary>
/// A station or gate decision's confidence band (research.md §4.2, confidence-routing):
/// high confidence acts, mid confidence asks staff to confirm, low confidence flags for review.
/// </summary>
public enum Flag
{
    None,
    Confirm,
    Review,
}
