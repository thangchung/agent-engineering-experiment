namespace ToolSearch.Gateway.Registry;

public sealed class ToolNotFoundException(string toolName)
    : Exception($"Tool '{toolName}' was not found.");

public sealed class ToolAccessDeniedException(string toolName)
    : Exception($"Access denied for tool '{toolName}'.");

public sealed class SyntheticToolRecursionException(string toolName)
    : Exception($"Synthetic tool recursion is blocked for '{toolName}'.");
