namespace McpServer.ToolSearch;

/// <summary>
/// Public projection used by synthetic Search and listing APIs.
/// </summary>
public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputJsonSchema,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsSynthetic);
