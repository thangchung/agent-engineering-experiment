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
    public static ISandboxRunner Create(
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        IReadOnlyList<string>? allowedBaseUrls = null)
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
                TimeoutSeconds = configuration.GetValue<int>("OpenSandbox:TimeoutSeconds", 5 * 60),
                ReadyTimeoutSeconds = configuration.GetValue<int>("OpenSandbox:ReadyTimeoutSeconds", 30),
                RequestTimeoutSeconds = configuration.GetValue<int>("OpenSandbox:RequestTimeoutSeconds", 30),
                UseServerProxy = configuration.GetValue<bool>("OpenSandbox:UseServerProxy", false),
                Timeout = TimeSpan.FromMilliseconds(configuration.GetValue<int>("CodeMode:TimeoutMs", 5000)),
                MaxToolCalls = configuration.GetValue<int>("CodeMode:MaxToolCalls", 10),
            };

            return new OpenSandboxRunner(options, loggerFactory);
        }

        return new LocalConstrainedRunner(
            timeout: TimeSpan.FromMilliseconds(configuration.GetValue<int>("CodeMode:TimeoutMs", 5000)),
            maxToolCalls: configuration.GetValue<int>("CodeMode:MaxToolCalls", 10),
            loggerFactory.CreateLogger<LocalConstrainedRunner>(),
            allowedBaseUrls);
    }

}
