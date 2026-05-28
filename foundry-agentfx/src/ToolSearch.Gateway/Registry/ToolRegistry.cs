using System.Text.Json;

namespace ToolSearch.Gateway.Registry;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyList<ToolDescriptor> _tools;
    private readonly Dictionary<string, ToolDescriptor> _byName;

    public ToolRegistry(IReadOnlyList<ToolDescriptor> tools)
    {
        _tools = tools;
        _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ToolDescriptor> GetVisibleTools(UserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _tools.Where(t => t.IsVisible(context)).ToArray();
    }

    public ToolDescriptor? FindByName(string name, UserContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);
        if (!_byName.TryGetValue(name, out ToolDescriptor? tool))
            return null;
        return tool.IsVisible(context) ? tool : null;
    }

    public async Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(context);
        if (!_byName.TryGetValue(name, out ToolDescriptor? tool))
            throw new ToolNotFoundException(name);
        if (!tool.IsVisible(context))
            throw new ToolAccessDeniedException(name);
        return await tool.Handler(arguments, ct);
    }
}
