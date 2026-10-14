using Microsoft.Extensions.DependencyInjection;

namespace DecisionModel.Client;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DecisionModelClient"/> as a typed HTTP client against <paramref name="baseUrl"/>,
    /// with the standard resilience handler (DecisionModel reads are idempotent, so retries are safe -
    /// research.md B11 contrasts this with the OpenAI-compatible chat client, which must NOT get one).
    /// </summary>
    public static IServiceCollection AddDecisionModelClient(
        this IServiceCollection services,
        string baseUrl,
        Action<DecisionModelOptions>? configure = null)
    {
        var options = new DecisionModelOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<DecisionModelClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
