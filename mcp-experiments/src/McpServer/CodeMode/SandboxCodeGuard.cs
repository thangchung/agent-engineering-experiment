namespace McpServer.CodeMode;

/// <summary>
/// Shared guards applied by all sandbox runners to enforce code-mode isolation.
/// Centralised here so that both <see cref="LocalConstrainedRunner"/> and
/// <see cref="OpenSandboxRunner"/> enforce the same policy without duplicating logic.
/// </summary>
internal static class SandboxCodeGuard
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="code"/> contains a call to
    /// a tool-search meta-tool that is forbidden inside code mode.
    /// </summary>
    internal static bool ContainsForbiddenMetaToolUsage(string code) =>
        code.Contains("call_tool(", StringComparison.Ordinal) ||
        code.Contains("search_tools(", StringComparison.Ordinal) ||
        code.Contains("search(", StringComparison.Ordinal) ||
        code.Contains("get_schema(", StringComparison.Ordinal) ||
        code.Contains("await call_tool(", StringComparison.Ordinal);
}
