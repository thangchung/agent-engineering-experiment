using Microsoft.Agents.AI;
using ModelContextProtocol.Client;

namespace AgentService.Providers;

/// <summary>
/// Extension methods to create Ollama-based AI agents.
/// </summary>
public static class OllamaAgentExtensions
{
    /// <summary>
    /// Creates an Ollama ChatClientAgent using the official Microsoft Agent Framework pattern.
    /// </summary>
    public static ChatClientAgent CreateOllamaAgent(
        this IServiceProvider services,
        string ollamaEndpoint,
        string model,
        string mcpToolsUrl,
        IList<McpClientTool> mcpTools,
        string? instructions = null,
        string? name = null,
        string? description = null)
    {
        var loggerFactory = services.GetService<ILoggerFactory>();

        return OllamaAgentFactory.Create(
            endpoint: ollamaEndpoint,
            model: model,
            mcpTools: mcpTools,
            instructions: instructions,
            name: name,
            description: description,
            loggerFactory: loggerFactory);
    }
}
