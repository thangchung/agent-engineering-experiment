using System.Diagnostics;
using System.ComponentModel;
using System.Text.Json;
using McpServer.Registry;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;

namespace McpServer.CodeMode;

/// <summary>
/// Code-mode MCP handlers: <c>Search</c>, <c>GetSchema</c>, <c>get_execute_syntax</c>, and <c>Execute</c>.
/// These form the staged discovery → schema → Execute workflow for LLM-driven code execution.
/// </summary>
[McpServerToolType]
public static class CodeModeHandlers
{
    /// <summary>
    /// Staged discovery: returns a brief list of tools matching the query for code-mode
    /// planning. Follow up with <c>GetSchema</c> to retrieve full parameter schemas.
    /// </summary>
    [McpServerTool(Name = "search"), Description("Discover tools by query with optional detail, tag filter, and limit. Use before GetSchema and Execute in a multi-step code-mode workflow.")]
    public static DiscoverySearchResponse Search(
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory,
        [Description("Search query for tool discovery")] string query,
        [Description("Detail level: Brief (default), Detailed, or Full. Also accepts 0, 1, or 2.")] JsonElement? detail = null,
        [Description("Optional tag filters. When omitted, all tags are included.")] IReadOnlyList<string>? tags = null,
        [Description("Optional maximum results. Defaults to server discovery default.")] int? limit = null)
    {
        SchemaDetailLevel resolvedDetail = detail.HasValue ? ParseSchemaDetailLevel(detail.Value, SchemaDetailLevel.Brief) : SchemaDetailLevel.Brief;
        DiscoverySearchResponse response = discoveryTools.Search(query, context, resolvedDetail, tags, limit);

        Activity.Current?.SetTag("mcp.handler.name", nameof(Search));
        Activity.Current?.SetTag("mcp.handler.results.count", response.Results.Count);
        Activity.Current?.SetTag("mcp.handler.results.totalMatched", response.TotalMatched);
        Activity.Current?.SetTag("mcp.handler.results.toolNames", string.Join(", ", response.Results.Select(static r => r.Name)));

        ILogger logger = loggerFactory.CreateLogger(typeof(CodeModeHandlers));
        logger.LogInformation(
            "[codemode] {HandlerName} returned {ResultCount} tools out of {TotalMatched} matches for query {Query}: {ToolNames}.",
            nameof(Search),
            response.Results.Count,
            response.TotalMatched,
            query,
            response.Results.Select(static r => r.Name));

        return response;
    }

    /// <summary>
    /// Returns input schemas for a set of tool names with optional detail and missing-name reporting.
    /// </summary>
    [McpServerTool(Name = "get_schema"),
     Description(
         "Retrieve input schemas for tool names. Pass a list via toolNames or a single name via name. Default detail is Detailed markdown, and missing tool names are reported.")]
    public static SchemaLookupResponse GetSchema(
        [FromServices] DiscoveryTools discoveryTools,
        [FromServices] UserContext context,
        [FromServices] ILoggerFactory loggerFactory,
        [Description("List of tool names to retrieve schemas for")]
        IReadOnlyList<string>? toolNames = null,
        [Description("Single tool name shorthand - equivalent to toolNames with one entry")]
        string? name = null,
        [Description(
            "Schema verbosity: Brief name-only, Detailed compact markdown, Full full JSON schema. Also accepts 0, 1, or 2.")]
        JsonElement? detail = null)
    {
        IReadOnlyList<string> requestedToolNames = ResolveSchemaToolNames(toolNames, name);
        SchemaDetailLevel resolvedDetail = detail.HasValue
            ? ParseSchemaDetailLevel(detail.Value, SchemaDetailLevel.Detailed)
            : SchemaDetailLevel.Detailed;
        SchemaLookupResponse response = discoveryTools.GetSchema(requestedToolNames, context, resolvedDetail);

        Activity.Current?.SetTag("mcp.handler.name", nameof(GetSchema));
        Activity.Current?.SetTag("mcp.handler.results.count", response.Results.Count);
        Activity.Current?.SetTag("mcp.handler.results.toolNames",
            string.Join(", ", response.Results.Select(static s => s.Name)));
        Activity.Current?.SetTag("mcp.handler.results.missingCount", response.Missing.Count);

        ILogger logger = loggerFactory.CreateLogger(typeof(CodeModeHandlers));
        logger.LogInformation(
            "[codemode] {HandlerName} returned {SchemaCount} schemas for tools {ToolNames}. Detail: {Detail}. Missing: {MissingTools}.",
            nameof(GetSchema),
            response.Results.Count,
            response.Results.Select(static s => s.Name),
            resolvedDetail,
            response.Missing.Count > 0 ? string.Join(", ", response.Missing) : "(none)");

        return response;
    }

    /// <summary>
    /// Returns the exact code syntax guide for the <c>execute</c> tool based on the active runner.
    /// Call this before writing code for <c>execute</c> to learn required language and conventions.
    /// </summary>
    [McpServerTool(Name = "get_execute_syntax"), Description("Returns the code syntax guide for execute. Call this first to confirm runner expectations before writing code.")]
    public static string GetExecuteSyntax([FromServices] ISandboxRunner runner)
    {
        return runner.SyntaxGuide;
    }

    /// <summary>
    /// Executes constrained code and returns only the final value.
    /// The required code syntax depends on the configured runner - call <c>get_execute_syntax</c> first.
    /// </summary>
    [McpServerTool(Name = "execute"), Description("""
        Execute constrained code and return the final result.
        IMPORTANT: call Search, then GetSchema, then get_execute_syntax before Execute.
        The runner will reject code written in the wrong style with an error message.
        """)]
    public static async Task<object?> Execute(
        [Description("Code string written in the syntax returned by get_execute_syntax.")] string code,
        [FromServices] ExecuteTool executeTool,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(CodeModeHandlers));
        logger.LogInformation(
            "[codemode] {HandlerName} executing code ({CodeLength} chars).",
            nameof(Execute),
            code.Length);

        try
        {
            ExecuteResponse response = await executeTool.ExecuteAsync(code, ct);

            logger.LogInformation(
                "[codemode] {HandlerName} completed successfully. HasResult: {HasResult}.",
                nameof(Execute),
                response.FinalValue is not null);

            return response.FinalValue;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "[codemode] {HandlerName} failed: {Error}.",
                nameof(Execute),
                ex.Message);

            // Return execution errors as content so the LLM reports them to the user
            // instead of interpreting an MCP error as "tool broken, try fallback".
            return $"[Execute error] {ex.Message}";
        }
    }

    private static SchemaDetailLevel ParseSchemaDetailLevel(JsonElement detail, SchemaDetailLevel defaultValue)
    {
        return detail.ValueKind switch
        {
            JsonValueKind.Undefined => defaultValue,
            JsonValueKind.Null => defaultValue,
            JsonValueKind.String => ParseSchemaDetailLevel(detail.GetString(), defaultValue),
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

    private static SchemaDetailLevel ParseSchemaDetailLevel(string? detail, SchemaDetailLevel defaultValue)
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
}
