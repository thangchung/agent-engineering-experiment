using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using McpServer.Registry;
using McpServer.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace McpServer.ToolSearch;

/// <summary>
/// Tool-Search-tool MCP handlers: <c>SearchTools</c>, <c>CallTool</c>, and <c>find_and_call</c>.
/// These let the LLM discover and invoke real tools via the internal registry.
/// </summary>
[McpServerToolType]
public static class ToolSearchHandlers
{
    /// <summary>
    /// Searches the tool catalog and returns matching tool definitions including their schemas.
    /// Use this to discover what real tools are available before calling <c>CallTool</c>.
    /// </summary>
    [McpServerTool(Name = "search_tools"), Description("Search the internal tool catalog using a natural language query. Returns tool names, descriptions, and input schemas.")]
    public static IReadOnlyList<ToolDefinition> SearchTools(
        [Description("Natural language Search query, e.g. \"Search breweries by city\"")] string query,
        [Description("Maximum number of results to return")] int limit,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory)
    {
        IReadOnlyList<ToolDefinition> results = metaTools.SearchTools(query, limit, context);

        Activity.Current?.SetTag("mcp.handler.name", nameof(SearchTools));
        Activity.Current?.SetTag("mcp.handler.results.count", results.Count);
        Activity.Current?.SetTag("mcp.handler.results.toolNames", string.Join(", ", results.Select(static r => r.Name)));

        ILogger logger = loggerFactory.CreateLogger(typeof(ToolSearchHandlers));
        logger.LogInformation(
            "MCP handler {HandlerName} returned {ResultCount} tools for query {Query} with limit {Limit}: {ToolNames}.",
            nameof(SearchTools),
            results.Count,
            query,
            limit,
            results.Select(static t => t.Name));

        return results;
    }

    /// <summary>
    /// Invokes a real tool through the registry by name. Synthetic meta-tools (SearchTools,
    /// CallTool, Search, GetSchema, Execute) cannot be called through this proxy to prevent
    /// recursive loops.
    /// </summary>
    [McpServerTool(Name = "call_tool"),
     Description(
         "Invoke a real tool by name with JSON arguments. Blocked for synthetic meta-tools to prevent recursion.")]
    public static async Task<object?> CallTool(
        [Description("Exact name of the real tool to invoke (case-insensitive)")]
        string name,
        [Description("Tool input arguments as a JSON object")]
        JsonElement arguments,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        CancellationToken ct)
    {
        return await metaTools.CallToolAsync(name, arguments, context, ct);
    }
}
