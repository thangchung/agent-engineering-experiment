using Spectre.Console.Cli;

namespace CoffeeshopCli.Infrastructure;

/// <summary>
/// Type resolver for Spectre.Console.Cli DI integration.
/// Wraps IServiceProvider to resolve command dependencies.
/// </summary>
public sealed class TypeResolver : ITypeResolver
{
    private readonly IServiceProvider _provider;

    public TypeResolver(IServiceProvider provider)
    {
        _provider = provider;
    }

    public object? Resolve(Type? type)
    {
        return type is null ? null : _provider.GetService(type);
    }
}
