using System.Text.Json;

namespace McpServer.Registry;

/// <summary>
/// Defines registry operations for finding and invoking tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Returns tools visible to the provided user context.
    /// </summary>
    IReadOnlyList<ToolDescriptor> GetVisibleTools(UserContext context);

    /// <summary>
    /// Returns one tool by name if visible to the provided context.
    /// </summary>
    ToolDescriptor? FindByName(string name, UserContext context);

    /// <summary>
    /// Invokes a named tool using provided JSON arguments.
    /// </summary>
    Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct);
}
