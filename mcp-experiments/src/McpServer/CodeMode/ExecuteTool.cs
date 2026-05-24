using System.Diagnostics;
using McpServer.CodeMode.Validation;

namespace McpServer.CodeMode;

/// <summary>
/// Executes a validated plan through the configured sandbox runner.
/// </summary>
public sealed class ExecuteTool(ISandboxRunner runner, IPythonSyntaxValidator syntaxValidator)
{
    private static readonly ActivitySource ActivitySource = new("McpServer.CodeMode.ExecuteTool");

    public ExecuteTool(ISandboxRunner runner)
        : this(runner, new NullSyntaxValidator())
    {
    }
    /// <summary>
    /// Executes code and returns only the final value to reduce token usage.
    /// </summary>
    public async Task<ExecuteResponse> ExecuteAsync(string code, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        using Activity? activity = ActivitySource.StartActivity("codemode.Execute", ActivityKind.Internal);
        activity?.SetTag("mcp.code.length", code.Length);

        await syntaxValidator.EnsureValidAsync(code, ct);

        RunnerResult result = await runner.RunAsync(code, ct);

        activity?.SetTag("mcp.Execute.callCount", result.CallsExecuted);
        activity?.SetTag("mcp.Execute.hasFinalValue", result.FinalValue is not null);

        return new ExecuteResponse(result.FinalValue);
    }
}
