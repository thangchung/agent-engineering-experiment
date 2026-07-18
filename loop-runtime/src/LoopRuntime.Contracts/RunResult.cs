namespace LoopRuntime.Contracts;

public sealed record RunResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut);
