using Microsoft.Extensions.Configuration;

namespace CoffeeshopCli.Configuration;

/// <summary>
/// Loads CLI configuration from appsettings.json and environment variables.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Load configuration using precedence: env vars > appsettings.{env}.json > appsettings.json > defaults.
    /// </summary>
    /// <param name="basePath">Optional base directory for appsettings resolution (defaults to current directory; used in tests).</param>
    public static CliConfig Load(string? basePath = null)
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        var builder = new ConfigurationBuilder();

        if (basePath is not null)
            builder.SetBasePath(basePath);

        builder
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();

        var configuration = builder.Build();

        var skillsDirectory = configuration["Discovery:SkillsDirectory"] ?? "./skills";

        var cfg = new CliConfig
        {
            Discovery = new DiscoveryConfig { SkillsDirectory = skillsDirectory },
            Mcp = new McpConfig(),
            Hosting = new HostingConfig()
        };

        var serversSection = configuration.GetSection("Mcp:Servers");
        if (serversSection.Exists())
        {
            var servers = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in serversSection.GetChildren())
            {
                var server = new McpServerConfig
                {
                    Command = child["Command"] ?? "",
                    Args = child.GetSection("Args").GetChildren().Select(c => c.Value ?? "").ToList(),
                    WorkingDirectory = child["WorkingDirectory"] ?? "."
                };
                servers[child.Key] = server;
            }
            cfg = cfg with { Mcp = new McpConfig { Servers = servers } };
        }

        var hostingSection = configuration.GetSection("Hosting");
        if (hostingSection.Exists())
        {
            cfg = cfg with
            {
                Hosting = new HostingConfig
                {
                    EnableHttpMcpBridge = bool.TryParse(hostingSection["EnableHttpMcpBridge"], out var flag) && flag,
                    HttpMcpRoute        = hostingSection["HttpMcpRoute"] ?? "/mcp",
                    HealthRoute         = hostingSection["HealthRoute"]  ?? "/healthz",
                    Urls                = hostingSection["Urls"]         ?? "http://0.0.0.0:8080"
                }
            };
        }

        return cfg;
    }
}
