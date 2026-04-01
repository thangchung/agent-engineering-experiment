namespace McpServer.Cli;

/// <summary>
/// Represents a normalized outcome of invoking one tool from CLI.
/// </summary>
internal sealed record ToolInvocationResult(bool IsSuccess, string ToolName, object? Output, string? ErrorMessage);
