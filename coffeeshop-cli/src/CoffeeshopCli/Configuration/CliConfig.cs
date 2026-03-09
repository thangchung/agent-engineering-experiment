namespace CoffeeshopCli.Configuration;

/// <summary>
/// Root CLI configuration loaded from file/env/CLI options.
/// </summary>
public sealed record CliConfig
{
    public DiscoveryConfig Discovery { get; init; } = new();
    public McpConfig Mcp { get; init; } = new();
}

/// <summary>
/// Discovery-related configuration.
/// </summary>
public sealed record DiscoveryConfig
{
    public string SkillsDirectory { get; init; } = "./skills";
}

/// <summary>
/// MCP server configuration section.
/// </summary>
public sealed record McpConfig
{
    public Dictionary<string, McpServerConfig> Servers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Single MCP server process config.
/// </summary>
public sealed record McpServerConfig
{
    public string Command { get; init; } = "";
    public List<string> Args { get; init; } = new();
    public string WorkingDirectory { get; init; } = ".";
}
