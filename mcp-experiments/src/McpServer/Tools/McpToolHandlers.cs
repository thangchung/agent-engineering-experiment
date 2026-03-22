using System.ComponentModel;
using System.Text.Json;
using McpServer.CodeMode;
using McpServer.Registry;
using Microsoft.AspNetCore.Mvc;
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
        [FromServices] UserContext context)
    {
        return metaTools.SearchTools(query, limit, context);
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
        [Description("Search query for tool discovery")] string query,
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [Description("Detail level: Brief (default), Detailed, or Full")] SchemaDetailLevel detail = SchemaDetailLevel.Brief,
        [Description("Optional tag filters. When omitted, all tags are included.")] IReadOnlyList<string>? tags = null,
        [Description("Optional maximum results. Defaults to server discovery default.")] int? limit = null)
    {
        return discoveryTools.Search(query, context, detail, tags, limit);
    }

    /// <summary>
    /// Returns input schemas for a set of tool names with optional detail and missing-name reporting.
    /// </summary>
    [McpServerTool, Description("Retrieve input schemas for tool names. Default detail is Detailed markdown, and missing tool names are reported.")]
    public static SchemaLookupResponse get_schema(
        [Description("List of tool names to retrieve schemas for")] IReadOnlyList<string> toolNames,
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [Description("Schema verbosity: Brief name-only, Detailed compact markdown, Full full JSON schema")] SchemaDetailLevel detail = SchemaDetailLevel.Detailed)
    {
        return discoveryTools.GetSchema(toolNames, context, detail);
    }

    /// <summary>
    /// Executes constrained code that can chain tool calls and returns only the final value.
    /// Intermediate values inside the code block are not returned to reduce token cost.
    ///
    /// <para>Supported statement forms only — no JavaScript expressions, method chains, or transformations:</para>
    /// <list type="bullet">
    /// <item><c>[const|let|var] varName = await call_tool("name", {"key": value});</c></item>
    /// <item><c>return varName;</c></item>
    /// <item><c>return await call_tool("name", {"key": value});</c></item>
    /// </list>
    /// <example>
    /// <code>
    /// const a = await call_tool("brewery_search", {"by_city": "Portland"});
    /// return a;
    /// </code>
    /// </example>
    /// </summary>
    [McpServerTool, Description("""
        Execute constrained code that chains await call_tool() statements and returns the final result.
        ONLY these statement forms are allowed — no JavaScript expressions, map/filter, or method chains:
                    [const|let|var] varName = await call_tool("toolName", {"argKey": argValue});
                    return varName;
                    return await call_tool("toolName", {"argKey": argValue});
                To project specific fields from an array result, use the built-in select_fields virtual tool:
                    const projected = await call_tool("select_fields", {data: varName, fields: "field1,field2"});
                Argument object keys may be quoted or unquoted. Always end statements with a semicolon.
        """)]
    public static async Task<object?> execute(
        [Description("Code string using only supported statement forms: variable assignments with await call_tool() and a return statement.")] string code,
        [FromServices] ExecuteTool executeTool,
        [FromServices] UserContext context,
        CancellationToken ct)
    {
        ExecuteResponse response = await executeTool.ExecuteAsync(code, context, ct);
        return response.FinalValue;
    }
}
