namespace LoopRuntime.Contracts;

public sealed record Verdict(
    bool Ok,
    Feedback? Feedback,
    RunResult RunResult);
