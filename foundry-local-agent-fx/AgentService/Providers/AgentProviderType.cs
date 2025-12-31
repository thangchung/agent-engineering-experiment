namespace AgentService.Providers;

/// <summary>
/// Supported agent provider types.
/// </summary>
public enum AgentProviderType
{
    /// <summary>
    /// Microsoft Foundry Local provider (OpenAI-compatible API).
    /// </summary>
    FoundryLocal,

    /// <summary>
    /// Ollama provider using OllamaSharp client.
    /// </summary>
    Ollama
}
