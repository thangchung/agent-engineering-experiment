using System.Text.Json;

namespace McpServer.Registry;

/// <summary>
/// Delegate used by the registry to execute a tool handler.
/// </summary>
/// <param name="arguments">Input arguments represented as JSON.</param>
/// <param name="ct">Cancellation token for cooperative cancellation.</param>
/// <returns>Tool output as an arbitrary object.</returns>
public delegate Task<object?> ToolHandler(JsonElement arguments, CancellationToken ct);

/// <summary>
/// Canonical metadata and execution contract for one tool.
/// </summary>
public sealed record ToolDescriptor(
    string Name,
    string Description,
    string InputJsonSchema,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsSynthetic,
    Func<UserContext, bool> IsVisible,
    ToolHandler Handler);
