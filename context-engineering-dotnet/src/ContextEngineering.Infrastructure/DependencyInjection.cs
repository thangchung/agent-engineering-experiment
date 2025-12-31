using ContextEngineering.Core.Interfaces;
using ContextEngineering.Infrastructure.Data;
using ContextEngineering.Infrastructure.Plugins;
using ContextEngineering.Infrastructure.Repositories;
using ContextEngineering.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContextEngineering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Repositories
        services.AddScoped<IScratchpadRepository, ScratchpadRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        
        // Services
        services.AddSingleton<ITokenCounter, TiktokenCounter>();
        
        // Plugins (AIFunction tools for LLM)
        services.AddScoped<ScratchpadPlugin>();
        services.AddScoped<ConversationPlugin>();
        
        return services;
    }
}
