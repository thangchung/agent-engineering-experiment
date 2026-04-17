using System.Text.RegularExpressions;

namespace McpServer.CodeMode;

/// <summary>
/// Shared guards applied by all sandbox runners to enforce code-mode isolation.
/// Centralised here so that both <see cref="LocalConstrainedRunner"/> and
/// <see cref="OpenSandboxRunner"/> enforce the same policy without duplicating logic.
/// </summary>
internal static partial class SandboxCodeGuard
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="code"/> contains a call to
    /// a tool-Search meta-tool that is forbidden inside code mode.
    /// </summary>
    internal static bool ContainsForbiddenMetaToolUsage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return ForbiddenMetaToolCallPattern().IsMatch(code);
    }

    [GeneratedRegex(
        @"(?<![\w""'])\b(?:CallTool|SearchTools|Search|GetSchema|Execute)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenMetaToolCallPattern();
}
