namespace McpServer.CodeMode;

/// <summary>
/// Result returned by a sandbox runner after executing code.
/// </summary>
public sealed record RunnerResult(object? FinalValue, int CallsExecuted);

/// <summary>
/// Final execute response returned to the caller.
/// </summary>
public sealed record ExecuteResponse(object? FinalValue);
