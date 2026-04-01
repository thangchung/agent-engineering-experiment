using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using McpServer.Registry;
using McpServer.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// Tool-search-tool MCP handlers: <c>search_tools</c>, <c>call_tool</c>, and <c>find_and_call</c>.
/// These let the LLM discover and invoke real tools via the internal registry.
/// </summary>
[McpServerToolType]
public static class ToolSearchHandlers
{
    /// <summary>
    /// Searches the tool catalog and returns matching tool definitions including their schemas.
    /// Use this to discover what real tools are available before calling <c>call_tool</c>.
    /// </summary>
    [McpServerTool, Description("Search the internal tool catalog using a natural language query. Returns tool names, descriptions, and input schemas.")]
    public static IReadOnlyList<ToolDefinition> search_tools(
        [Description("Natural language search query, e.g. \"search breweries by city\"")] string query,
        [Description("Maximum number of results to return")] int limit,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory)
    {
        IReadOnlyList<ToolDefinition> results = metaTools.SearchTools(query, limit, context);

        Activity.Current?.SetTag("mcp.handler.name", nameof(search_tools));
        Activity.Current?.SetTag("mcp.handler.results.count", results.Count);
        Activity.Current?.SetTag("mcp.handler.results.toolNames", string.Join(", ", results.Select(static r => r.Name)));

        ILogger logger = loggerFactory.CreateLogger(typeof(ToolSearchHandlers));
        logger.LogInformation(
            "MCP handler {HandlerName} returned {ResultCount} tools for query {Query} with limit {Limit}: {ToolNames}.",
            nameof(search_tools),
            results.Count,
            query,
            limit,
            results.Select(static t => t.Name));

        return results;
    }

    /// <summary>
    /// Invokes a real tool through the registry by name. Synthetic meta-tools (search_tools,
    /// call_tool, search, get_schema, execute) cannot be called through this proxy to prevent
    /// recursive loops.
    /// </summary>
    [McpServerTool, Description("Invoke a real tool by name with JSON arguments. Blocked for synthetic meta-tools to prevent recursion.")]
    public static async Task<object?> call_tool(
        [Description("Exact name of the real tool to invoke (case-insensitive)")] string name,
        [Description("Tool input arguments as a JSON object")] JsonElement arguments,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        CancellationToken ct)
    {
        return await metaTools.CallToolAsync(name, arguments, context, ct);
    }

    /// <summary>
    /// Single-step tool discovery and invocation.
    /// Searches for a matching tool by natural language intent and immediately invokes it
    /// if a single best match is found. Reduces two-turn search + call_tool flows to one turn
    /// for the common single-action case.
    /// </summary>
    [McpServerTool, Description("Find the best-matching tool by natural language intent and invoke it in one step. Use when you are confident about the action. Returns the tool result directly, or a disambiguation list if multiple tools match equally.")]
    public static async Task<object?> find_and_call(
        [Description("Natural language description of what to do, e.g. 'search breweries in Seattle'")] string intent,
        [Description("Arguments to pass to the matched tool as a JSON object")] JsonElement arguments,
        [FromServices] IToolSearcher searcher,
        [FromServices] MetaTools metaTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(ToolSearchHandlers));
        IReadOnlyList<ToolDescriptor> candidates = searcher.Search(intent, 3, context);

        if (candidates.Count == 0)
        {
            logger.LogInformation(
                "MCP handler {HandlerName}: no matching tools found for intent '{Intent}'.",
                nameof(find_and_call), intent);
            return "No matching tool found. Use search to browse available tools.";
        }

        ToolDescriptor best = candidates[0];

        // Exact match by name or only one candidate - invoke directly
        if (candidates.Count == 1 || best.Name.Equals(intent, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "MCP handler {HandlerName}: invoking tool '{ToolName}' for intent '{Intent}'.",
                nameof(find_and_call), best.Name, intent);

            Activity.Current?.SetTag("mcp.handler.name", nameof(find_and_call));
            Activity.Current?.SetTag("mcp.handler.matched.tool", best.Name);

            return await metaTools.CallToolAsync(best.Name, arguments, context, ct);
        }

        // Ambiguous - return top candidates for the LLM to select from
        logger.LogInformation(
            "MCP handler {HandlerName}: ambiguous intent '{Intent}', returning {CandidateCount} candidates.",
            nameof(find_and_call), intent, candidates.Count);

        Activity.Current?.SetTag("mcp.handler.name", nameof(find_and_call));
        Activity.Current?.SetTag("mcp.handler.candidates.count", candidates.Count);

        return candidates.Select(t => new { t.Name, t.Description }).ToArray();
    }
}
