using System.Diagnostics;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using McpServer.CodeMode;
using McpServer.Registry;
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
    [McpServerTool, Description("Retrieve input schemas for tool names. Accepts toolNames plus common aliases such as names, tools, toolName, or name. Default detail is Detailed markdown, and missing tool names are reported.")]
    public static SchemaLookupResponse get_schema(
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [Description("Preferred parameter: list of tool names to retrieve schemas for")] IReadOnlyList<string>? toolNames = null,
        [Description("Alias for toolNames")] IReadOnlyList<string>? names = null,
        [Description("Alias for toolNames")] IReadOnlyList<string>? tools = null,
        [Description("Singular alias for one tool name")] string? toolName = null,
        [Description("Singular alias for one tool name")] string? name = null,
        [Description("Schema verbosity: Brief name-only, Detailed compact markdown, Full full JSON schema. Also accepts 0, 1, or 2.")] JsonElement? detail = null)
    {
        IReadOnlyList<string> requestedToolNames = ResolveSchemaToolNames(toolNames, names, tools, toolName, name);
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
            // the caller's default rather than throwing — the LLM intent is ambiguous.
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
        IReadOnlyList<string>? names,
        IReadOnlyList<string>? tools,
        string? toolName,
        string? name)
    {
        List<string> requested = [];
        AppendNames(requested, toolNames);
        AppendNames(requested, names);
        AppendNames(requested, tools);
        AppendName(requested, toolName);
        AppendName(requested, name);

        if (requested.Count == 0)
        {
            throw new ArgumentException(
                "Provide at least one tool name using toolNames, names, tools, toolName, or name.",
                "arguments");
        }

        return requested
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AppendNames(List<string> requested, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (string value in values)
        {
            AppendName(requested, value);
        }
    }

    private static void AppendName(List<string> requested, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        requested.Add(value.Trim());
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
    /// The required code syntax depends on the configured runner — call <c>get_execute_syntax</c> first.
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
