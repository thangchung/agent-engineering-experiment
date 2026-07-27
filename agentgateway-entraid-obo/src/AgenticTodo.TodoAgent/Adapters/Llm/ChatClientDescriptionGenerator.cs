using AgenticTodo.TodoAgent.Domain.Ports;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticTodo.TodoAgent.Adapters.Llm;

public sealed class ChatClientDescriptionGenerator(IChatClient chatClient) : IDescriptionGenerator
{
    private const string Instructions =
        "Given a todo name, write one concise description of at most 40 words. Return only the description text.";

    public async Task<string> GenerateAsync(string name, CancellationToken cancellationToken)
    {
        var agent = chatClient.AsAIAgent(Instructions);
        var response = await agent.RunAsync(name, cancellationToken: cancellationToken);
        return response.Text;
    }
}
