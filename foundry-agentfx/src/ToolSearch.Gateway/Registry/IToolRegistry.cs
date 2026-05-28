using System.Text.Json;

namespace ToolSearch.Gateway.Registry;

public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> GetVisibleTools(UserContext context);
    ToolDescriptor? FindByName(string name, UserContext context);
    Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct);
}
