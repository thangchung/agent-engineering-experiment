using ModelContextProtocol.Client;

namespace AgentService.Providers;

/// <summary>
/// Extension methods to create FoundryLocalAgent easily.
/// </summary>
public static class FoundryLocalAgentExtensions
{
    /// <summary>
    /// Creates a FoundryLocalAgent with OpenTelemetry instrumentation from configuration.
    /// </summary>
    /// <param name="services">Service provider for DI</param>
    /// <param name="foundryEndpoint">Foundry Local endpoint</param>
    /// <param name="model">Model name</param>
    /// <param name="mcpToolsUrl">MCP tools server URL</param>
    /// <param name="mcpTools">MCP tools to register</param>
    /// <param name="instructions">System instructions</param>
    /// <param name="name">Agent name</param>
    /// <param name="description">Agent description</param>
    /// <param name="enableSensitiveData">Enable capture of message content for OTel</param>
    /// <returns>An InstrumentedFoundryLocalAgent with full OpenTelemetry support</returns>
    public static InstrumentedFoundryLocalAgent CreateFoundryLocalAgent(
        this IServiceProvider services,
        string foundryEndpoint,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        string? instructions = null,
        string? name = null,
        string? description = null,
        bool? enableSensitiveData = null)
    {
        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var loggerFactory = services.GetService<ILoggerFactory>();

        return FoundryLocalAgentFactory.Create(
            endpoint: foundryEndpoint,
            model: model,
            mcpToolsUrl: mcpToolsUrl,
            mcpTools: mcpTools,
            httpClient: httpClientFactory.CreateClient(),
            instructions: instructions,
            name: name,
            description: description,
            loggerFactory: loggerFactory,
            enableSensitiveData: enableSensitiveData);
    }
}
