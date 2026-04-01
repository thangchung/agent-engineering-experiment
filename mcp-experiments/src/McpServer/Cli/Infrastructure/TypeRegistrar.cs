using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace McpServer.Cli.Infrastructure;

/// <summary>
/// Bridges Spectre.Console.Cli type registration with Microsoft DI.
/// </summary>
internal sealed class TypeRegistrar(IServiceCollection services, IServiceProvider? serviceProvider = null) : ITypeRegistrar
{
    private readonly IServiceCollection _services = services;
    private IServiceProvider? _serviceProvider = serviceProvider;

    /// <summary>
    /// Returns the single provider instance used for command resolution.
    /// </summary>
    internal IServiceProvider GetServiceProvider()
    {
        _serviceProvider ??= _services.BuildServiceProvider();
        return _serviceProvider;
    }

    /// <inheritdoc />
    public ITypeResolver Build()
    {
        return new TypeResolver(GetServiceProvider());
    }

    /// <inheritdoc />
    public void Register(Type service, Type implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    /// <inheritdoc />
    public void RegisterInstance(Type service, object implementation)
    {
        _services.AddSingleton(service, implementation);
    }

    /// <inheritdoc />
    public void RegisterLazy(Type service, Func<object> factory)
    {
        _services.AddSingleton(service, _ => factory());
    }
}
