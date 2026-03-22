namespace McpServer;

/// <summary>
/// Static accessor for the application's root <see cref="IServiceProvider"/>.
///
/// Tool handler lambdas are registered in the tool catalog before <c>app.Build()</c>
/// runs, so they cannot receive DI services via constructor injection. This thin
/// accessor is set to <c>app.Services</c> immediately after <c>Build()</c> and
/// allows handlers to resolve services on demand.
///
/// Only use this for singletons or transient services that are safe to resolve
/// from the root scope. For scoped services, create an explicit scope inside the handler.
/// </summary>
internal static class ToolServiceProvider
{
    private static IServiceProvider? _root;

    /// <summary>
    /// Set once in Program.cs immediately after <c>app.Build()</c>.
    /// Throws if assigned more than once to prevent accidental replacement.
    /// </summary>
    internal static IServiceProvider Root
    {
        get => _root ?? throw new InvalidOperationException("ToolServiceProvider.Root has not been set. Ensure it is assigned in Program.cs after app.Build().");
        set
        {
            if (_root is not null)
            {
                throw new InvalidOperationException("ToolServiceProvider.Root is already set and cannot be reassigned.");
            }

            _root = value;
        }
    }

    /// <summary>Resolves a required service from the root provider.</summary>
    internal static T GetRequiredService<T>() where T : notnull =>
        Root.GetRequiredService<T>();
}
