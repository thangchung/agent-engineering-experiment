namespace Claw.Core;

using System.Text.Json;

public sealed record ToolDefinition(
    string Name,
    string Description,
    string InputJsonSchema,
    IReadOnlyList<string> Tags,
    bool IsPinned,
    bool IsSynthetic);

public interface IToolSearchClient
{
    Task<IReadOnlyList<ToolDefinition>> SearchToolsAsync(string query, int limit, CancellationToken ct = default);
    Task<object?> CallToolAsync(string name, JsonElement arguments, CancellationToken ct = default);
}
