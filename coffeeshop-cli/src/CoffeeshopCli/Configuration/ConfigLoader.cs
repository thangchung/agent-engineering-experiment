using Microsoft.Extensions.Configuration;

namespace CoffeeshopCli.Configuration;

/// <summary>
/// Loads CLI configuration from defaults, file, and environment variables.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Load configuration using precedence: cli options > env vars > file > defaults.
    /// </summary>
    public static CliConfig Load(string? explicitConfigPath = null, string? cliSkillsDirectory = null)
    {
        var defaultConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config",
            "coffeeshop-cli",
            "config.json"
        );

        var configPath = explicitConfigPath ?? defaultConfigPath;

        var builder = new ConfigurationBuilder();

        if (File.Exists(configPath))
        {
            builder.AddJsonFile(configPath, optional: true, reloadOnChange: false);
        }

        builder.AddEnvironmentVariables(prefix: "COFFEESHOP_");

        var configuration = builder.Build();

        var fileSkillsDir = configuration["discovery:skills_directory"];
        var envSkillsDir = configuration["SKILLS_DIR"];

        var skillsDirectory = cliSkillsDirectory
            ?? envSkillsDir
            ?? fileSkillsDir
            ?? "./skills";

        var cfg = new CliConfig
        {
            Discovery = new DiscoveryConfig { SkillsDirectory = skillsDirectory },
            Mcp = new McpConfig()
        };

        var serversSection = configuration.GetSection("mcp:servers");
        if (serversSection.Exists())
        {
            var servers = new Dictionary<string, McpServerConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in serversSection.GetChildren())
            {
                var server = new McpServerConfig
                {
                    Command = child["command"] ?? "",
                    Args = child.GetSection("args").GetChildren().Select(c => c.Value ?? "").ToList(),
                    WorkingDirectory = child["working_directory"] ?? "."
                };
                servers[child.Key] = server;
            }
            cfg = cfg with { Mcp = new McpConfig { Servers = servers } };
        }

        return cfg;
    }
}
