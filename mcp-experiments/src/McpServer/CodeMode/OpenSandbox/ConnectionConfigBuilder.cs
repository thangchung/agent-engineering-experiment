using OpenSandbox.Config;

namespace McpServer.CodeMode.OpenSandbox;

/// <summary>
/// Helper for building OpenSandbox ConnectionConfig from configuration and environment variables.
/// </summary>
public static class ConnectionConfigBuilder
{
    /// <summary>
    /// Builds a ConnectionConfig from runner options.
    /// </summary>
    /// <param name="options">OpenSandbox runner options.</param>
    /// <returns>Configured ConnectionConfig.</returns>
    public static ConnectionConfig Build(OpenSandboxRunnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var connectionProtocol = options.Protocol == ConnectionProtocol.Https
            ? ConnectionProtocol.Https
            : ConnectionProtocol.Http;

        return new ConnectionConfig(new ConnectionConfigOptions
        {
            Domain = options.Domain,
            ApiKey = string.IsNullOrWhiteSpace(options.ApiKey) ? null : options.ApiKey,
            Protocol = connectionProtocol,
            RequestTimeoutSeconds = options.RequestTimeoutSeconds,
            UseServerProxy = options.UseServerProxy,
        });
    }

    /// <summary>
    /// Parses connection protocol from string (http or https).
    /// </summary>
    /// <param name="protocolString">Protocol string (http/https or null).</param>
    /// <returns>ConnectionProtocol enum value.</returns>
    public static ConnectionProtocol ParseProtocol(string? protocolString)
    {
        return string.Equals(protocolString, "https", StringComparison.OrdinalIgnoreCase)
            ? ConnectionProtocol.Https
            : ConnectionProtocol.Http;
    }
}
