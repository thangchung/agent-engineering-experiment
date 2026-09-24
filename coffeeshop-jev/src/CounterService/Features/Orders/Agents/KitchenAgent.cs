using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Agents;

/// <summary>The kitchen persona (research.md §5.3): writes a short prep ticket for food lines.</summary>
public static class KitchenAgentFactory
{
    public const string Instructions =
        """
        You are the kitchen staff at a coffee shop. You will be given a JSON list of food lines
        (name, quantity).

        Reply with ONLY a JSON object of this exact shape, no markdown fences, no other text:
        {"items":[{"name":"<name>","qty":<qty>,"steps":["<step 1>","<step 2>"],"minutes":<int>}]}

        For each line, write 1 to 3 short prep steps in English and your best-guess whole-minute
        prep time as "minutes". Never add, drop, or change the quantity of an item you were
        given, and never invent an item you were not given.
        """;

    public static AIAgent Create(IChatClient chatClient, bool enableSensitiveData) =>
        chatClient.AsAIAgent(instructions: Instructions, name: "KitchenAgent", description: "Coffee shop kitchen staff.")
            .AsBuilder()
            .UseOpenTelemetry(sourceName: "CoffeeShop.Agents", configure: c => c.EnableSensitiveData = enableSensitiveData)
            .Build();
}
