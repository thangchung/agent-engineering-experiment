using System.Diagnostics;

namespace McpServer.CodeMode;

/// <summary>
/// Executes a validated plan through the configured sandbox runner.
/// </summary>
public sealed class ExecuteTool(ISandboxRunner runner)
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.ExecuteTool");
    /// <summary>
    /// Executes code and returns only the final value to reduce token usage.
    /// </summary>
    public async Task<ExecuteResponse> ExecuteAsync(string code, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using Activity? activity = ActivitySource.StartActivity("codemode.Execute", ActivityKind.Internal);
        activity?.SetTag("mcp.code.length", code.Length);

        RunnerResult result = await runner.RunAsync(code, ct);

        activity?.SetTag("mcp.Execute.callCount", result.CallsExecuted);
        activity?.SetTag("mcp.Execute.hasFinalValue", result.FinalValue is not null);

        return new ExecuteResponse(result.FinalValue);
    }
}
