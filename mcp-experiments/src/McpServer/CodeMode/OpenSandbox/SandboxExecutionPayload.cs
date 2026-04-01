namespace McpServer.CodeMode.OpenSandbox;

/// <summary>
/// Payload returned by the Python wrapper script.
/// Maps to the JSON envelope returned from exec'd Python code.
/// </summary>
internal sealed record SandboxExecutionPayload(
    bool Ok,
    object? FinalValue,
    string? Error,
    string? Traceback,
    string? Stdout,
    string? Stderr);
