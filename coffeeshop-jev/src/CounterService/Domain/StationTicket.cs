namespace CounterService.Domain;

public sealed record TicketItem
{
    public required string Name { get; init; }

    public required int Qty { get; init; }

    public IReadOnlyList<string>? Steps { get; init; }

    public int? Minutes { get; init; }
}

/// <summary>
/// What one station (barista or kitchen) prepared. research.md §5.2 StationExecutor: an
/// empty order skips the LLM call entirely (<see cref="Empty"/>); an agent failure never lets
/// the exception escape - it falls back to the original coffeeshop-agent's deterministic text
/// (<see cref="Fallback"/>), flagged for review (research.md B4/B8).
/// </summary>
public sealed record StationTicket
{
    public required Station Station { get; init; }

    public required IReadOnlyList<TicketItem> Items { get; init; }

    /// <summary>True when this ticket is a Fallback, not a real agent-written ticket.</summary>
    public bool IsFallback { get; init; }

    public static StationTicket Empty(Station station) => new()
    {
        Station = station,
        Items = [],
    };

    public static StationTicket Fallback(Station station, IReadOnlyList<OrderLine> lines)
    {
        var verb = station == Station.Barista ? "made" : "cooked";
        var itemsText = string.Join(", ", lines.Select(l => $"{l.Qty}x {l.Name}"));
        return new StationTicket
        {
            Station = station,
            IsFallback = true,
            Items =
            [
                new TicketItem
                {
                    Name = $"{itemsText} {verb}.",
                    Qty = 1,
                },
            ],
        };
    }
}
