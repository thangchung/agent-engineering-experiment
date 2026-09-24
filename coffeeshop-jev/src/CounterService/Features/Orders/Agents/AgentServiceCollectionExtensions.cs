using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders.Agents;

public static class AgentServiceCollectionExtensions
{
    /// <summary>Registers the 3 keyed agents. Requires <see cref="IChatClient"/> to already be
    /// registered (Common/AiSetup.cs's <c>AddOpenAiChatClient</c>).</summary>
    public static IServiceCollection AddOrderAgents(this IServiceCollection services)
    {
        services.AddKeyedSingleton<AIAgent>(AgentKeys.Counter, (sp, _) =>
            CounterAgentFactory.Create(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));

        services.AddKeyedSingleton<AIAgent>(AgentKeys.Barista, (sp, _) =>
            BaristaAgentFactory.Create(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));

        services.AddKeyedSingleton<AIAgent>(AgentKeys.Kitchen, (sp, _) =>
            KitchenAgentFactory.Create(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<IHostEnvironment>().IsDevelopment()));

        return services;
    }
}
