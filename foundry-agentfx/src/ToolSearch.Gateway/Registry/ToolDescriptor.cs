using System.Text.Json;

namespace ToolSearch.Gateway.Registry;

public delegate Task<object?> ToolHandler(JsonElement arguments, CancellationToken ct);

public sealed record ToolDescriptor(
    string Name,
    string Description,
    string InputJsonSchema,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsSynthetic,
    Func<UserContext, bool> IsVisible,
    ToolHandler Handler);
