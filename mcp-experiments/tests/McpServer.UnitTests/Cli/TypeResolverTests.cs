using McpServer.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace McpServer.UnitTests.Cli;

public sealed class TypeResolverTests
{
    [Fact]
    public void Dispose_UsesAsyncDisposal_WhenProviderContainsAsyncOnlyServices()
    {
        ServiceCollection services = new();
        services.AddSingleton<AsyncOnlyDisposable>();

        ServiceProvider provider = services.BuildServiceProvider();
        provider.GetRequiredService<AsyncOnlyDisposable>();

        TypeResolver resolver = new(provider);

        resolver.Dispose();

        Assert.True(AsyncOnlyDisposable.WasDisposed);
        AsyncOnlyDisposable.WasDisposed = false;
    }

    private sealed class AsyncOnlyDisposable : IAsyncDisposable
    {
        public static bool WasDisposed { get; set; }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}