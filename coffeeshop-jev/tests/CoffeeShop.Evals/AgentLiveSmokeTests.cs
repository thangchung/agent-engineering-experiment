using System.ClientModel;
using CounterService.Features.Orders.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CoffeeShop.Evals;

/// <summary>
/// tasks.md T07 AC4: a real call to the configured OpenAI-compatible deployment. Skipped
/// unless OPENAI_BASE_URL/OPENAI_API_KEY/OPENAI_MODEL are all set (LiveOpenAiFactAttribute).
/// </summary>
public class AgentLiveSmokeTests
{
    [LiveOpenAiFact]
    public async Task CounterAgent_SayHi_RepliesInEnglish_NonEmpty()
    {
        var endpoint = new Uri(EvalConfig.OpenAiBaseUrl!);
        var key = EvalConfig.OpenAiApiKey!;
        var model = EvalConfig.OpenAiModel!;

        var openAiClient = new OpenAIClient(new ApiKeyCredential(key), new OpenAIClientOptions { Endpoint = endpoint });
        IChatClient chatClient = openAiClient.GetChatClient(model).AsIChatClient();

        var agent = CounterAgentFactory.Create(chatClient, enableSensitiveData: false);
        var response = await agent.RunAsync([new ChatMessage(ChatRole.User, "say hi")]);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }
}
