namespace CounterService.Domain;

/// <summary>
/// One line of an order, after extraction (name/qty from the LLM, price from the catalog -
/// research.md §7: "price always comes from the catalog, never the LLM") and after the station
/// split (research.md §4.2).
/// </summary>
public sealed record OrderLine
{
    public required string Name { get; init; }

    public required int Qty { get; init; }

    /// <summary>Always the catalog price for <see cref="Name"/>, never LLM-supplied.</summary>
    public required decimal Price { get; init; }

    /// <summary>True when <see cref="Qty"/> was clamped down to the 1..20 limit (research.md Q5:
    /// the reply must tell the customer when this happens).</summary>
    public bool Clamped { get; init; }

    /// <summary>Set by the station split (StationSplitExecutor); null before that.</summary>
    public Station? Station { get; init; }

    /// <summary>Set by the station split; <see cref="Domain.Flag.None"/> before that.</summary>
    public Flag Flag { get; init; } = Flag.None;
}
