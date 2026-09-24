using CounterService.Domain;

namespace CounterService.Features.Orders.Workflow;

/// <summary>
/// Pure decision function for the Split step (research.md §4.2, Jev confidence-routing).
/// Confidence bands go to STAFF (Confirm/Review badges), never back to the customer - asking
/// "is a croissant food?" makes no sense to a customer.
/// </summary>
public static class StationPolicy
{
    /// <summary>docs.typesafe.ai/patterns/confidence-routing: below this, flag for staff review.</summary>
    public const double ReviewThreshold = 0.6;

    /// <summary>Between Review and this, flag for staff to confirm; above it, act with no flag.</summary>
    public const double ConfirmThreshold = 0.85;

    public const string ChoiceBarista = "barista";

    /// <summary>Choice values from the Split's per-line <c>station_i</c> question.</summary>
    public static (Station Station, Flag Flag) Assign(string stationChoice, double confidence)
    {
        // research.md's rule: food or "other" -> Kitchen. Only an exact "barista" choice routes there.
        var station = string.Equals(stationChoice, ChoiceBarista, StringComparison.OrdinalIgnoreCase)
            ? Station.Barista
            : Station.Kitchen;

        var flag = confidence switch
        {
            < ReviewThreshold => Flag.Review,
            <= ConfirmThreshold => Flag.Confirm,
            _ => Flag.None,
        };

        return (station, flag);
    }
}
