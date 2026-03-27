using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using McpServer.CodeMode;
using McpServer.Registry;
using McpServer.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

/// <summary>
/// MCP-registered tool handlers that are discovered by <see cref="McpServerToolTypeAttribute"/>
/// and exposed via the SSE transport. Each method delegates to the corresponding internal
/// domain service, keeping MCP protocol concerns separate from business logic.
///
/// <para>
/// These five tools form the published surface the LLM interacts with.
/// Real tools (e.g. brewery_search) live only inside <see cref="Registry.IToolRegistry"/>
/// and must be reached through <c>call_tool</c> or <c>execute</c>.
/// </para>
/// </summary>
[McpServerToolType]
public static class McpToolHandlers
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

        ILogger logger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
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
        ILogger logger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
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

    /// <summary>
    /// Staged discovery: returns a brief list of tools matching the query for code-mode
    /// planning. Follow up with <c>get_schema</c> to retrieve full parameter schemas.
    /// </summary>
    [McpServerTool, Description("Discover tools by query with optional detail, tag filter, and limit. Use before get_schema and execute in a multi-step code-mode workflow.")]
    public static DiscoverySearchResponse search(
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory,
        [Description("Search query for tool discovery")] string query,
        [Description("Detail level: Brief (default), Detailed, or Full. Also accepts 0, 1, or 2.")] JsonElement? detail = null,
        [Description("Optional tag filters. When omitted, all tags are included.")] IReadOnlyList<string>? tags = null,
        [Description("Optional maximum results. Defaults to server discovery default.")] int? limit = null)
    {
        SchemaDetailLevel resolvedDetail = detail.HasValue ? ParseSchemaDetailLevel(detail.Value, SchemaDetailLevel.Brief, nameof(detail)) : SchemaDetailLevel.Brief;
        DiscoverySearchResponse response = discoveryTools.Search(query, context, resolvedDetail, tags, limit);

        Activity.Current?.SetTag("mcp.handler.name", nameof(search));
        Activity.Current?.SetTag("mcp.handler.results.count", response.Results.Count);
        Activity.Current?.SetTag("mcp.handler.results.totalMatched", response.TotalMatched);
        Activity.Current?.SetTag("mcp.handler.results.toolNames", string.Join(", ", response.Results.Select(static r => r.Name)));

        ILogger logger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
        logger.LogInformation(
            "MCP handler {HandlerName} returned {ResultCount} tools out of {TotalMatched} matches for query {Query}: {ToolNames}.",
            nameof(search),
            response.Results.Count,
            response.TotalMatched,
            query,
            response.Results.Select(static r => r.Name));

        return response;
    }

    /// <summary>
    /// Returns input schemas for a set of tool names with optional detail and missing-name reporting.
    /// </summary>
    [McpServerTool, Description("Retrieve input schemas for tool names. Pass a list via toolNames or a single name via name. Default detail is Detailed markdown, and missing tool names are reported.")]
    public static SchemaLookupResponse get_schema(
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [Description("List of tool names to retrieve schemas for")] IReadOnlyList<string>? toolNames = null,
        [Description("Single tool name shorthand - equivalent to toolNames with one entry")] string? name = null,
        [Description("Schema verbosity: Brief name-only, Detailed compact markdown, Full full JSON schema. Also accepts 0, 1, or 2.")] JsonElement? detail = null)
    {
        IReadOnlyList<string> requestedToolNames = ResolveSchemaToolNames(toolNames, name);
        SchemaDetailLevel resolvedDetail = detail.HasValue ? ParseSchemaDetailLevel(detail.Value, SchemaDetailLevel.Detailed, nameof(detail)) : SchemaDetailLevel.Detailed;
        return discoveryTools.GetSchema(requestedToolNames, context, resolvedDetail);
    }

    private static SchemaDetailLevel ParseSchemaDetailLevel(JsonElement detail, SchemaDetailLevel defaultValue, string parameterName)
    {
        return detail.ValueKind switch
        {
            JsonValueKind.Undefined => defaultValue,
            JsonValueKind.Null => defaultValue,
            JsonValueKind.String => ParseSchemaDetailLevel(detail.GetString(), defaultValue, parameterName),
            JsonValueKind.Number when detail.TryGetInt32(out int numericValue) => numericValue switch
            {
                0 => SchemaDetailLevel.Brief,
                1 => SchemaDetailLevel.Detailed,
                2 => SchemaDetailLevel.Full,
                _ => defaultValue,
            },
            // For booleans, arrays, objects, or any other unrecognised type, fall back to
            // the caller's default rather than throwing - the LLM intent is ambiguous.
            _ => defaultValue,
        };
    }

    private static SchemaDetailLevel ParseSchemaDetailLevel(string? detail, SchemaDetailLevel defaultValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return defaultValue;
        }

        string normalized = detail.Trim();
        if (Enum.TryParse<SchemaDetailLevel>(normalized, ignoreCase: true, out SchemaDetailLevel parsed))
        {
            return parsed;
        }

        return normalized switch
        {
            "0" => SchemaDetailLevel.Brief,
            "1" => SchemaDetailLevel.Detailed,
            "2" => SchemaDetailLevel.Full,
            _ => defaultValue,
        };
    }

    private static IReadOnlyList<string> ResolveSchemaToolNames(
        IReadOnlyList<string>? toolNames,
        string? name)
    {
        List<string> requested = [];

        if (toolNames is not null)
        {
            foreach (string toolName in toolNames)
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    requested.Add(toolName.Trim());
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            requested.Add(name.Trim());
        }

        if (requested.Count == 0)
        {
            throw new ArgumentException(
                "Provide at least one tool name using toolNames or name.",
                "arguments");
        }

        return requested
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Returns the exact code syntax guide for the <c>execute</c> tool based on the active runner.
    /// Call this BEFORE writing code for <c>execute</c> to learn the required language and conventions.
    /// </summary>
    [McpServerTool, Description("Returns the code syntax guide for the execute tool. Call this first to learn whether to write DSL or Python code before calling execute.")]
    public static string get_execute_syntax([FromServices] ISandboxRunner runner)
    {
        return runner.SyntaxGuide;
    }

    /// <summary>
    /// Executes constrained code and returns only the final value.
    /// The required code syntax depends on the configured runner - call <c>get_execute_syntax</c> first.
    /// </summary>
    [McpServerTool, Description("""
        Execute constrained code and return the final result.
        IMPORTANT: call get_execute_syntax first to learn the exact syntax required by the active runner.
        The runner will reject code written in the wrong style with an error message.
        """)]
    public static async Task<object?> execute(
        [Description("Code string written in the syntax returned by get_execute_syntax.")] string code,
        [FromServices] ExecuteTool executeTool,
        CancellationToken ct)
    {
        ExecuteResponse response = await executeTool.ExecuteAsync(code, ct);
        return response.FinalValue;
    }
}
