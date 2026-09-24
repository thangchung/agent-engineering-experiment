using Microsoft.Extensions.DependencyInjection;

namespace Jev.Client;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="JevClient"/> as a typed HTTP client against <paramref name="baseUrl"/>,
    /// with the standard resilience handler (Jev reads are idempotent, so retries are safe -
    /// research.md B11 contrasts this with the OpenAI-compatible chat client, which must NOT get one).
    /// </summary>
    public static IServiceCollection AddJevClient(
        this IServiceCollection services,
        string baseUrl,
        Action<JevOptions>? configure = null)
    {
        var options = new JevOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddHttpClient<JevClient>(client =>
            {
                client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            })
            .AddStandardResilienceHandler();

        return services;
    }
}
