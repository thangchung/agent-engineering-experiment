using Microsoft.Extensions.Configuration;

namespace McpServer.CodeMode;

/// <summary>
/// Creates the configured sandbox runner implementation.
/// </summary>
public static class SandboxRunnerFactory
{
    /// <summary>
    /// Creates an <see cref="ISandboxRunner"/> from app configuration.
    /// </summary>
    public static ISandboxRunner Create(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        string runnerName = configuration["CodeMode:Runner"]?.Trim() ?? "local";

        if (string.Equals(runnerName, "opensandbox", StringComparison.OrdinalIgnoreCase))
        {
            string? domain = configuration["OpenSandbox:Domain"];
            if (string.IsNullOrWhiteSpace(domain))
            {
                throw new InvalidOperationException("OpenSandbox:Domain is required when CodeMode:Runner=opensandbox.");
            }

            OpenSandboxRunnerOptions options = new()
            {
                Domain = domain,
                ApiKey = configuration["OpenSandbox:ApiKey"],
                Image = configuration["OpenSandbox:Image"] ?? "ubuntu",
                TimeoutSeconds = ParseIntOrDefault(configuration["OpenSandbox:TimeoutSeconds"], 5 * 60),
                ReadyTimeoutSeconds = ParseIntOrDefault(configuration["OpenSandbox:ReadyTimeoutSeconds"], 30),
                RequestTimeoutSeconds = ParseIntOrDefault(configuration["OpenSandbox:RequestTimeoutSeconds"], 30),
                UseServerProxy = ParseBoolOrDefault(configuration["OpenSandbox:UseServerProxy"], false),
                Timeout = TimeSpan.FromMilliseconds(ParseIntOrDefault(configuration["CodeMode:TimeoutMs"], 5000)),
                MaxToolCalls = ParseIntOrDefault(configuration["CodeMode:MaxToolCalls"], 10),
            };

            return new OpenSandboxRunner(options, loggerFactory);
        }

        return new LocalConstrainedRunner(
            timeout: TimeSpan.FromMilliseconds(ParseIntOrDefault(configuration["CodeMode:TimeoutMs"], 5000)),
            maxToolCalls: ParseIntOrDefault(configuration["CodeMode:MaxToolCalls"], 10),
            loggerFactory.CreateLogger<LocalConstrainedRunner>());
    }

    /// <summary>
    /// Parses an integer configuration value with fallback default.
    /// </summary>
    private static int ParseIntOrDefault(string? value, int defaultValue)
    {
        return int.TryParse(value, out int parsed) ? parsed : defaultValue;
    }

    /// <summary>
    /// Parses a boolean configuration value with fallback default.
    /// </summary>
    private static bool ParseBoolOrDefault(string? value, bool defaultValue)
    {
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }
}
