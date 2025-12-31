using Microsoft.Agents.AI;
using ModelContextProtocol.Client;

namespace AgentService.Providers;

/// <summary>
/// Factory for creating AI agents based on provider type.
/// Implements early-return pattern for clean provider selection.
/// </summary>
public static class AgentProviderFactory
{
    /// <summary>
    /// Creates an AI agent based on the specified provider type.
    /// </summary>
    public static AIAgent Create(
        IServiceProvider services,
        AgentProviderType providerType,
        AgentProviderConfig config,
        IList<McpClientTool> mcpTools,
        string? instructions = null,
        string? name = null,
        string? description = null)
    {
        return providerType switch
        {
            // Ollama uses official ChatClientAgent with FunctionInvokingChatClient
            AgentProviderType.Ollama => services.CreateOllamaAgent(
                ollamaEndpoint: config.Endpoint,
                model: config.Model,
                mcpToolsUrl: config.McpToolsUrl,
                mcpTools: mcpTools,
                instructions: instructions,
                name: name ?? "OllamaAgent",
                description: description ?? "AI Agent powered by Ollama"),

            AgentProviderType.FoundryLocal => services.CreateFoundryLocalAgent(
                foundryEndpoint: config.Endpoint,
                model: config.Model,
                mcpToolsUrl: config.McpToolsUrl,
                mcpTools: mcpTools,
                instructions: instructions,
                name: name ?? "FoundryLocalAgent",
                description: description ?? "AI Agent powered by Foundry Local"),

            _ => throw new ArgumentException($"Unknown provider type: {providerType}", nameof(providerType))
        };
    }

    /// <summary>
    /// Parses provider type from string (case-insensitive).
    /// </summary>
    public static AgentProviderType ParseProviderType(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
            return AgentProviderType.FoundryLocal;

        return provider.Trim().ToLowerInvariant() switch
        {
            "ollama" => AgentProviderType.Ollama,
            "foundrylocal" or "foundry" or "foundry-local" => AgentProviderType.FoundryLocal,
            _ => AgentProviderType.FoundryLocal
        };
    }
}

/// <summary>
/// Configuration for an agent provider.
/// </summary>
public record AgentProviderConfig(
    string Endpoint,
    string Model,
    string McpToolsUrl);
