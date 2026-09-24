namespace CounterService.Domain;

public enum OrderStatus
{
    Completed,
    Rejected,
}

/// <summary>The final output of one order-placement workflow run (research.md §5.2/§5.4).</summary>
public sealed record OrderResult
{
    public required OrderStatus Status { get; init; }

    public IReadOnlyList<StationTicket> Tickets { get; init; } = [];

    public decimal Total { get; init; }

    public required string Message { get; init; }

    public RejectReason? Reason { get; init; }

    public static OrderResult Rejected(RejectReason reason, string message) => new()
    {
        Status = OrderStatus.Rejected,
        Message = message,
        Reason = reason,
    };
}
