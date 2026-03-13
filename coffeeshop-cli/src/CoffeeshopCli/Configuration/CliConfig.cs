namespace CoffeeshopCli.Configuration;

/// <summary>
/// HTTP MCP bridge hosting configuration.
/// </summary>
public sealed record HostingConfig
{
    public bool EnableHttpMcpBridge { get; init; } = false;
    public string HttpMcpRoute { get; init; } = "/mcp";
    public string HealthRoute { get; init; } = "/healthz";
    public string Urls { get; init; } = "http://0.0.0.0:8080";
}

/// <summary>
/// Root CLI configuration loaded from appsettings and environment variables.
/// </summary>
public sealed record CliConfig
{
    public DiscoveryConfig Discovery { get; init; } = new();
    public McpConfig Mcp { get; init; } = new();
    public HostingConfig Hosting { get; init; } = new();
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
