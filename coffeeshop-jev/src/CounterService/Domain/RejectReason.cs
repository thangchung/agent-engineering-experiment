namespace CounterService.Domain;

/// <summary>Why an order never reached the kitchen/barista (research.md §5.2 rule, §11.5).</summary>
public enum RejectReason
{
    /// <summary>The request was not about ordering food or drinks.</summary>
    OffTopic,

    /// <summary>A guard blocked the request (research.md §11/§12).</summary>
    Blocked,

    /// <summary>The clarify loop ran out of asks (research.md §4.1, MaxAsks).</summary>
    AskCap,

    /// <summary>Jev was unreachable or errored (research.md G4, fail-closed at the gate).</summary>
    JevDown,

    /// <summary>The customer asked to cancel (research.md §12 U4).</summary>
    Cancelled,
}
