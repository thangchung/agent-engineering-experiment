namespace CoffeeshopCli.Errors;

/// <summary>
/// MCP error - MCP server connection or tool call failed.
/// Exit code: 3
/// </summary>
public sealed class McpError : CliError
{
    public McpError(string message, Dictionary<string, object>? details = null)
        : base("mcp", message, details)
    {
    }

    public override int ExitCode => 3;
}

/// <summary>
/// Skill error - skill invocation or execution failed.
/// Exit code: 4
/// </summary>
public sealed class SkillError : CliError
{
    public SkillError(string message, Dictionary<string, object>? details = null)
        : base("skill", message, details)
    {
    }

    public override int ExitCode => 4;
}
