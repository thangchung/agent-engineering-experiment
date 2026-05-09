using Microsoft.Extensions.Configuration;
using McpServer.CodeMode.Hyperlight;
using McpServer.CodeMode.Local;
using McpServer.CodeMode.OpenSandbox;

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

        string runnerName = configuration["CodeMode:Runner"]?.Trim() ?? "auto";
        int timeoutMs = configuration.GetValue<int>("CodeMode:TimeoutMs", 5000);
        int maxToolCalls = configuration.GetValue<int>("CodeMode:MaxToolCalls", 10);

        if (string.Equals(runnerName, "opensandbox", StringComparison.OrdinalIgnoreCase))
        {
            string? domain = configuration["OpenSandbox:Domain"] ?? "localhost:8080";

            OpenSandboxRunnerOptions options = new()
            {
                Domain = domain,
                ApiKey = configuration["OpenSandbox:ApiKey"],
                Image = configuration["OpenSandbox:Image"] ?? "python:3.12-slim",
                TimeoutSeconds = configuration.GetValue<int>("OpenSandbox:TimeoutSeconds", 5 * 60),
                ReadyTimeoutSeconds = configuration.GetValue<int>("OpenSandbox:ReadyTimeoutSeconds", 30),
                RequestTimeoutSeconds = configuration.GetValue<int>("OpenSandbox:RequestTimeoutSeconds", 30),
                UseServerProxy = configuration.GetValue<bool>("OpenSandbox:UseServerProxy", true),
                Timeout = TimeSpan.FromMilliseconds(timeoutMs),
                MaxToolCalls = maxToolCalls,
                AllowedBaseUrls = allowedBaseUrls,
            };

            return new OpenSandboxRunner(options, loggerFactory, allowedBaseUrls);
        }

        if (string.Equals(runnerName, "local", StringComparison.OrdinalIgnoreCase))
        {
            return new LocalConstrainedRunner(
                timeout: TimeSpan.FromMilliseconds(timeoutMs),
                maxToolCalls: maxToolCalls,
                loggerFactory.CreateLogger<LocalConstrainedRunner>(),
                allowedBaseUrls);
        }

        try
        {
            return new HyperlightSandboxRunner(
                timeout: TimeSpan.FromMilliseconds(timeoutMs),
                maxToolCalls: maxToolCalls,
                loggerFactory.CreateLogger<HyperlightSandboxRunner>(),
                allowedBaseUrls);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("McpServer.CodeMode.SandboxRunnerFactory")
                .LogWarning(ex, "Falling back to local sandbox runner because Hyperlight runner could not be created.");

            return new LocalConstrainedRunner(
                timeout: TimeSpan.FromMilliseconds(timeoutMs),
                maxToolCalls: maxToolCalls,
                loggerFactory.CreateLogger<LocalConstrainedRunner>(),
                allowedBaseUrls);
        }
    }

}
