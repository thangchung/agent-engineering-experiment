using System.ClientModel;
using CounterService.Features.Orders.Agents;
using Jev.Client;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CoffeeShop.Evals;

/// <summary>Builds the same clients the app builds (research.md §10.4: "prompts are shared, not
/// copied" - applies equally to the clients that carry them), pointed at whatever `EvalConfig`
/// resolved, for the live eval tests (each starts with an `EvalConfig.Require*()` call).</summary>
public static class EvalClients
{
    public static JevClient Jev() =>
        new(
            new HttpClient { BaseAddress = new Uri(EvalConfig.OpenJevUrl!.TrimEnd('/') + "/") },
            new JevOptions { ApiKey = EvalConfig.JevApiKey });

    public static IChatClient ChatClient()
    {
        var endpoint = new Uri(EvalConfig.OpenAiBaseUrl!);
        var openAiClient = new OpenAIClient(new ApiKeyCredential(EvalConfig.OpenAiApiKey!), new OpenAIClientOptions { Endpoint = endpoint });
        return openAiClient.GetChatClient(EvalConfig.OpenAiModel!).AsIChatClient();
    }

    public static AIAgent CounterAgent() => CounterAgentFactory.Create(ChatClient(), enableSensitiveData: true);

    public static AIAgent BaristaAgent() => BaristaAgentFactory.Create(ChatClient(), enableSensitiveData: true);
}
