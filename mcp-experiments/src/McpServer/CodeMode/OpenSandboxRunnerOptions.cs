namespace McpServer.CodeMode;

/// <summary>
/// Configuration values used by the OpenSandbox-backed runner.
/// </summary>
public sealed class OpenSandboxRunnerOptions
{
    /// <summary>
    /// OpenSandbox API domain, for example localhost:8080.
    /// </summary>
    public required string Domain { get; init; }

    /// <summary>
    /// Optional API key when server authentication is enabled.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Sandbox image used for preflight checks.
    /// </summary>
    public string Image { get; init; } = "python:3.12-slim";

    /// <summary>
    /// Sandbox TTL in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 5 * 60;

    /// <summary>
    /// Sandbox readiness timeout in seconds for CreateAsync health checks.
    /// </summary>
    public int ReadyTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Request timeout for OpenSandbox HTTP calls in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Whether to request server-proxied endpoint URLs.
    /// </summary>
    public bool UseServerProxy { get; init; }

    /// <summary>
    /// Timeout applied to constrained code execution.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum tool calls allowed in one execute block.
    /// </summary>
    public int MaxToolCalls { get; init; } = 10;
}
