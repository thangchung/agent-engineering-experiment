namespace McpServer.Registry;

/// <summary>
/// Raised when a tool cannot be found in the registry.
/// </summary>
public sealed class ToolNotFoundException(string toolName) : Exception($"Tool '{toolName}' was not found.")
{
}

/// <summary>
/// Raised when a tool is not visible for the current caller context.
/// </summary>
public sealed class ToolAccessDeniedException(string toolName) : Exception($"Access denied for tool '{toolName}'.")
{
}

/// <summary>
/// Raised when a synthetic tool attempts to call another synthetic tool recursively.
/// </summary>
public sealed class SyntheticToolRecursionException(string toolName) : Exception($"Synthetic tool recursion is blocked for '{toolName}'.")
{
}
