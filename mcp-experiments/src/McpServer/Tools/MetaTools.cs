using System.Text.Json;
using System.Diagnostics;
using McpServer.Registry;
using McpServer.Search;

namespace McpServer.Tools;

/// <summary>
/// Synthetic tool surface that exposes search and safe proxy invocation.
/// </summary>
public sealed class MetaTools(IToolRegistry registry, IToolSearcher searcher)
{
    private static readonly ActivitySource ActivitySource = new("McpServer.MetaTools");
    private static readonly HashSet<string> RecursiveSyntheticCalls =
        ["search_tools", "call_tool", "search", "get_schema", "execute"];

    /// <summary>
    /// Lists synthetic and pinned tools only.
    /// </summary>
    public IReadOnlyList<ToolDefinition> ListTools(UserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return registry
            .GetVisibleTools(context)
            .Where(tool => tool.IsPinned || tool.IsSynthetic)
            .Select(ToDefinition)
            .ToArray();
    }

    /// <summary>
    /// Searches visible tools and returns compact schema-bearing definitions.
    /// </summary>
    public IReadOnlyList<ToolDefinition> SearchTools(string query, int limit, UserContext context)
    {
        using Activity? activity = ActivitySource.StartActivity("meta.search_tools", ActivityKind.Internal);
        activity?.SetTag("mcp.query", query);
        activity?.SetTag("mcp.limit", limit);

        IReadOnlyList<ToolDefinition> results = searcher.Search(query, limit, context)
            .Select(ToDefinition)
            .ToArray();

        activity?.SetTag("mcp.results.count", results.Count);
        return results;
    }

    /// <summary>
    /// Invokes a real tool through the registry while blocking synthetic recursion.
    /// </summary>
    public async Task<object?> CallToolAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        using Activity? activity = ActivitySource.StartActivity("meta.call_tool", ActivityKind.Internal);
        activity?.SetTag("mcp.tool.name", name);

        if (RecursiveSyntheticCalls.Contains(name.ToLowerInvariant()))
        {
            activity?.SetTag("mcp.call.blocked", true);
            throw new SyntheticToolRecursionException(name);
        }

        object? result = await registry.InvokeAsync(name, arguments, context, ct);
        activity?.SetTag("mcp.call.blocked", false);
        return result;
    }

    /// <summary>
    /// Maps internal descriptor to synthetic response definition.
    /// </summary>
    private static ToolDefinition ToDefinition(ToolDescriptor tool)
    {
        return new ToolDefinition(
            tool.Name,
            tool.Description,
            tool.InputJsonSchema,
            tool.Tags,
            tool.IsPinned,
            tool.IsSynthetic);
    }
}
