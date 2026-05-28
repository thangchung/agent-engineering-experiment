using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ToolSearch.Gateway.Registry;
using ToolSearch.Gateway.ToolSearch;

namespace ToolSearch.Gateway;

[McpServerToolType]
public static class ToolSearchHandlers
{
    [McpServerTool(Name = "search_tools"),
     Description("Search the tool catalog using a natural language query. Returns tool names, descriptions, and input schemas. Call this before call_tool to discover available tools.")]
    public static IReadOnlyList<ToolDefinition> SearchTools(
        [Description("Natural language search query, e.g. 'place a coffee order'")] string query,
        [Description("Maximum number of results to return (recommend 5)")] int limit,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory)
    {
        IReadOnlyList<ToolDefinition> results = metaTools.SearchTools(query, limit, context);

        Activity.Current?.SetTag("gen_ai.operation.name", "search_tools");
        Activity.Current?.SetTag("mcp.tool.search.result_count", results.Count);

        loggerFactory.CreateLogger(typeof(ToolSearchHandlers))
            .LogInformation("search_tools returned {Count} tools for query '{Query}': [{Names}]",
                results.Count, query, string.Join(", ", results.Select(r => r.Name)));

        return results;
    }

    [McpServerTool(Name = "call_tool"),
     Description("Invoke a real tool by name with JSON arguments. Use search_tools first to discover available tools. Synthetic meta-tools cannot be called via this method.")]
    public static async Task<object?> CallTool(
        [Description("Exact name of the tool to invoke (case-insensitive)")] string name,
        [Description("Tool input arguments as a JSON object")] JsonElement arguments,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        CancellationToken ct)
    {
        Activity.Current?.SetTag("gen_ai.operation.name", "call_tool");
        Activity.Current?.SetTag("mcp.tool.name", name);

        return await metaTools.CallToolAsync(name, arguments, context, ct);
    }
}
