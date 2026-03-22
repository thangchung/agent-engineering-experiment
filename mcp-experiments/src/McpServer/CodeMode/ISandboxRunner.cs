using System.Text.Json;

namespace McpServer.CodeMode;

/// <summary>
/// Bridge delegate used by runners to invoke registered tools.
/// </summary>
public delegate Task<object?> ToolBridge(string toolName, JsonElement args, CancellationToken ct);

/// <summary>
/// Contract for execution backends used by execute flow.
/// </summary>
public interface ISandboxRunner
{
    /// <summary>
    /// Runs code using tool bridge with runtime limits and cancellation.
    /// </summary>
    Task<RunnerResult> RunAsync(string code, ToolBridge bridge, CancellationToken ct);
}
