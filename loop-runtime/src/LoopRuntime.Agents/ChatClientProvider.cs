using System.ClientModel;
using LoopRuntime.Agents.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace LoopRuntime.Agents;

/// <summary>
/// Registers <see cref="IChatClient"/> based on configuration.
/// Provider values: "fake" (deterministic) or "gateway" (OpenAI-compatible endpoint served by AgentGateway).
/// </summary>
public static class ChatClientProvider
{
    public const string FakeProvider = "fake";
    public const string GatewayProvider = "gateway";

    public static IServiceCollection AddChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["AI:Provider"]?.ToLowerInvariant() ?? FakeProvider;
        var captureContent = ShouldCaptureMessageContent(configuration);

        if (provider == GatewayProvider)
        {
            var llmEndpoint = configuration["AgentGateway:LlmEndpoint"]
                ?? configuration["AgentGateway__LlmEndpoint"]
                ?? throw new InvalidOperationException("AgentGateway:LlmEndpoint is required when AI:Provider=gateway.");

            var model = configuration["AI:Model"]
                ?? configuration["AI__Model"]
                ?? throw new InvalidOperationException("AI:Model is required when AI:Provider=gateway.");

            services.AddSingleton<IChatClient>(_ =>
            {
                var openAiClient = new OpenAIClient(
                    new ApiKeyCredential("agentgateway"),
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri($"{llmEndpoint.TrimEnd('/')}/v1")
                    });
                return new TaggingChatClient(openAiClient.GetChatClient(model).AsIChatClient(), captureContent);
            });
        }
        else
        {
            services.AddSingleton<IChatClient>(_ => new TaggingChatClient(new FakeChatClient(), captureContent));
        }

        return services;
    }

    private static bool ShouldCaptureMessageContent(IConfiguration configuration)
    {
        var value = configuration["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"]
            ?? configuration["Otel_Instrumentation_Genai_Capture_Message_Content"]
            ?? Environment.GetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT");

        return bool.TryParse(value, out var result) && result;
    }
}
