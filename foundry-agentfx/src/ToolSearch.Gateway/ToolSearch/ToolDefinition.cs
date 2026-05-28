namespace ToolSearch.Gateway.ToolSearch;

public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputJsonSchema,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsSynthetic);
