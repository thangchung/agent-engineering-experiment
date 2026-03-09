using CoffeeshopCli.Services;

namespace CoffeeshopCli.Mcp.Tools;

/// <summary>
/// Model-related tool definitions and operations.
/// </summary>
public sealed class ModelTools
{
    private readonly IDiscoveryService _discovery;

    public ModelTools(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public object ListModelsDefinition() => new
    {
        name = "list_models",
        description = "List available data models",
        annotations = new { readOnlyHint = true }
    };

    public object ShowModelDefinition() => new
    {
        name = "show_model",
        description = "Show details for a data model",
        annotations = new { readOnlyHint = true }
    };

    public IReadOnlyList<object> ListModels()
    {
        return _discovery.DiscoverModels()
            .Select(m => (object)new { name = m.Name, property_count = m.PropertyCount })
            .ToList();
    }
}
