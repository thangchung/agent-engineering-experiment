using System.Diagnostics;
using System.Text.Json;
using ToolSearch.Gateway.Registry;
using ToolSearch.Gateway.Search;

namespace ToolSearch.Gateway.ToolSearch;

public sealed class MetaTools(IToolRegistry registry, IToolSearcher searcher)
{
    private static readonly ActivitySource ActivitySource = new("ToolSearch.Gateway.MetaTools");
    private static readonly HashSet<string> RecursiveSyntheticCalls =
        ["searchtools", "calltool", "search_tools", "call_tool"];

    public IReadOnlyList<ToolDefinition> SearchTools(string query, int limit, UserContext context)
    {
        using Activity? activity = ActivitySource.StartActivity("meta.search_tools", ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "search_tools");
        activity?.SetTag("gen_ai.request.model", "n/a");
        activity?.SetTag("mcp.tool.search.query", query);
        activity?.SetTag("mcp.tool.search.limit", limit);

        IReadOnlyList<ToolDefinition> results = searcher.Search(query, limit, context)
            .Select(ToDefinition)
            .ToArray();

        activity?.SetTag("mcp.tool.search.result_count", results.Count);
        return results;
    }

    public async Task<object?> CallToolAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        using Activity? activity = ActivitySource.StartActivity("meta.call_tool", ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "call_tool");
        activity?.SetTag("mcp.tool.name", name);

        if (RecursiveSyntheticCalls.Contains(name.ToLowerInvariant()))
        {
            activity?.SetTag("mcp.tool.call.blocked", true);
            throw new SyntheticToolRecursionException(name);
        }

        object? result = await registry.InvokeAsync(name, arguments, context, ct);
        activity?.SetTag("mcp.tool.call.blocked", false);
        return result;
    }

    private static ToolDefinition ToDefinition(ToolDescriptor tool) =>
        new(tool.Name, tool.Description, tool.InputJsonSchema, tool.Tags, tool.IsPinned, tool.IsSynthetic);
}
