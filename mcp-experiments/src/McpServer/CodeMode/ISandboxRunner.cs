namespace McpServer.CodeMode;

/// <summary>
/// Contract for execution backends used by Execute flow.
/// </summary>
public interface ISandboxRunner
{
    /// <summary>
    /// Human-readable guide describing the code syntax this runner expects.
    /// Exposed via the <c>get_execute_syntax</c> MCP tool so the LLM can discover
    /// the correct language and conventions before calling <c>Execute</c>.
    /// </summary>
    string SyntaxGuide { get; }

    /// <summary>
    /// Runs code with runtime limits and cancellation.
    /// </summary>
    Task<RunnerResult> RunAsync(string code, CancellationToken ct);
}
