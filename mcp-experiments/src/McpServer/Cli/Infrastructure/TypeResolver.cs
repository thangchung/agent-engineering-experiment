using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace McpServer.Cli.Infrastructure;

/// <summary>
/// Resolves command dependencies through the underlying DI provider.
/// </summary>
internal sealed class TypeResolver(IServiceProvider serviceProvider) : ITypeResolver, IDisposable
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    /// <inheritdoc />
    public object? Resolve(Type? type)
    {
        if (type is null)
        {
            return null;
        }

        return _serviceProvider.GetService(type) ?? ActivatorUtilities.CreateInstance(_serviceProvider, type);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
            return;
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
