using McpServer.Registry;

namespace McpServer.CodeMode;

/// <summary>
/// Coordinates execution through the execute service.
/// </summary>
public sealed class WorkflowCoordinator(ExecuteTool executeTool)
{
    /// <summary>
    /// Executes code and returns the final value.
    /// </summary>
    public async Task<object?> RunAsync(string code, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(context);

        ExecuteResponse response = await executeTool.ExecuteAsync(code, ct);
        return response.FinalValue;
    }
}
