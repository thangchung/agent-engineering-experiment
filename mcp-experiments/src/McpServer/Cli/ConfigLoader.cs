using Microsoft.Extensions.Configuration;

namespace McpServer.Cli;

/// <summary>
/// Loads CLI-related hosting settings from configuration sources.
/// </summary>
public static class ConfigLoader
{
    /// <summary>
    /// Resolves CLI flags from configuration with safe defaults.
    /// </summary>
    /// <param name="configuration">Root application configuration.</param>
    /// <returns>CLI mode settings.</returns>
    public static CliConfig Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        bool enableCliMode = configuration.GetValue("Hosting:EnableCliMode", false);
        bool cliServeMode = configuration.GetValue("Hosting:CliServeMode", false);
        bool enableStatistic = configuration.GetValue("Hosting:EnableStatistic", false);
        return new CliConfig(enableCliMode, cliServeMode, enableStatistic);
    }
}
