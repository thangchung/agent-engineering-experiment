using System.Text.Json;

namespace McpServer.Registry;

/// <summary>
/// In-memory registry that enforces visibility checks before invocation.
/// A name-keyed dictionary provides O(1) lookups for <see cref="FindByName"/>
/// and <see cref="InvokeAsync"/> instead of O(n) linear scans.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ToolDescriptor> _tools;
    private readonly Dictionary<string, ToolDescriptor> _byName;

    /// <summary>
    /// Initializes the registry and builds the name index.
    /// </summary>
    public ToolRegistry(IReadOnlyList<ToolDescriptor> tools)
    {
        _tools = tools;
        _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns tools that are visible to the provided caller context.
    /// </summary>
    public IReadOnlyList<ToolDescriptor> GetVisibleTools(UserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _tools.Where(tool => tool.IsVisible(context)).ToArray();
    }

    /// <summary>
    /// Finds one visible tool by name, or null when not found.
    /// </summary>
    public ToolDescriptor? FindByName(string name, UserContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        if (!_byName.TryGetValue(name, out ToolDescriptor? tool))
        {
            return null;
        }

        return tool.IsVisible(context) ? tool : null;
    }

    /// <summary>
    /// Invokes a tool if it exists and is visible in the current caller context.
    /// </summary>
    public async Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        if (!_byName.TryGetValue(name, out ToolDescriptor? tool))
        {
            throw new ToolNotFoundException(name);
        }

        if (!tool.IsVisible(context))
        {
            throw new ToolAccessDeniedException(name);
        }

        return await tool.Handler(arguments, ct);
    }
}
