using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CounterService.Common;

/// <summary>
/// Wires the shared <see cref="IChatClient"/> to any OpenAI-compatible endpoint - Microsoft
/// Foundry's v1 endpoint, a local proxy (LiteLLM, vLLM, etc.), whatever OPENAI_BASE_URL points
/// at (research.md §2.1, §14 Q12) - no Entra needed.
/// No resilience handler is added here (research.md B11): the OpenAI SDK manages its own HTTP
/// pipeline, so there is nothing to stack a second retry layer on top of.
/// </summary>
public static class AiSetup
{
    /// <summary>Q15: no model configured falls back to this - the model this repo's own
    /// history (commit 4008895) already standardized on for other demos.</summary>
    public const string DefaultModel = "gpt-5.4-mini";

    public static IServiceCollection AddOpenAiChatClient(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var baseUrl = configuration["OPENAI_BASE_URL"];
            var apiKey = configuration["OPENAI_API_KEY"];
            var model = configuration["OPENAI_MODEL"];

            // research.md AppHost design: an unset endpoint/key must never crash startup - it
            // fails at the first real call instead (the workflow's own fallbacks handle that).
            var baseUri = string.IsNullOrWhiteSpace(baseUrl) ? new Uri("https://openai.invalid/v1/") : new Uri(baseUrl);
            // research.md B10: the OpenAI client requires a non-empty key even when the server enforces none.
            var key = string.IsNullOrWhiteSpace(apiKey) ? "unused" : apiKey;
            var modelName = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;

            var clientOptions = new OpenAIClientOptions { Endpoint = baseUri };
            var openAiClient = new OpenAIClient(new ApiKeyCredential(key), clientOptions);
            return openAiClient.GetChatClient(modelName).AsIChatClient();
        });

        return services;
    }
}
