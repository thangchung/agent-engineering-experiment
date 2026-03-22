using System.Text.Json;

namespace McpServer.Registry;

/// <summary>
/// In-memory registry that enforces visibility checks before invocation.
/// </summary>
public sealed class ToolRegistry(IReadOnlyList<ToolDescriptor> tools) : IToolRegistry
{
    private readonly IReadOnlyList<ToolDescriptor> _tools = tools;

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

        return _tools.FirstOrDefault(tool =>
            tool.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && tool.IsVisible(context));
    }

    /// <summary>
    /// Invokes a tool if it exists and is visible in the current caller context.
    /// </summary>
    public async Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);

        ToolDescriptor? tool = _tools.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (tool is null)
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
