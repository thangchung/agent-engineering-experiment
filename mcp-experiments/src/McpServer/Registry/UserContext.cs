namespace McpServer.Registry;

/// <summary>
/// Represents caller context used for visibility and authorization checks.
/// </summary>
public sealed record UserContext(bool IsAdmin = false);
