using System.Text.Json;
using McpServer.Registry;
using McpServer.ToolSearch;

namespace McpServer.Cli;

/// <summary>
/// Thin wrapper around MetaTools call flow for CLI command usage.
/// </summary>
internal sealed class ToolInvoker(MetaTools metaTools, UserContext userContext)
{
    /// <summary>
    /// Invokes one tool by name with JSON arguments.
    /// </summary>
    public async Task<ToolInvocationResult> InvokeAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        object? output = await metaTools.CallToolAsync(toolName, arguments, userContext, cancellationToken);
        return new ToolInvocationResult(IsSuccess: true, ToolName: toolName, Output: output, ErrorMessage: null);
    }
}
