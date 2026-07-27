using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace AgenticTodo.TodoAgent;

public static class ChatClientProvider
{
    public static IServiceCollection AddChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        var llmEndpoint = configuration["AgentGateway:LlmEndpoint"]
            ?? throw new InvalidOperationException("AgentGateway:LlmEndpoint is required.");
        var model = configuration["AI:Model"]
            ?? throw new InvalidOperationException("AI:Model is required.");
        var captureContent = ShouldCaptureMessageContent(configuration);

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

        return services;
    }

    private static bool ShouldCaptureMessageContent(IConfiguration configuration)
    {
        var value = configuration["OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT"]
            ?? Environment.GetEnvironmentVariable("OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT");

        return bool.TryParse(value, out var result) && result;
    }
}
