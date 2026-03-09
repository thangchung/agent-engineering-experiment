using CoffeeshopCli.Services;

namespace CoffeeshopCli.Mcp.Tools;

/// <summary>
/// Central registry for MCP tool metadata surfaced by the stdio server.
/// </summary>
public sealed class ToolRegistry
{
    private readonly ModelTools _modelTools;
    private readonly SkillTools _skillTools;
    private readonly OrderTools _orderTools;

    public ToolRegistry(ModelTools modelTools, SkillTools skillTools, OrderTools orderTools)
    {
        _modelTools = modelTools;
        _skillTools = skillTools;
        _orderTools = orderTools;
    }

    public IReadOnlyList<object> ListTools()
    {
        return
        [
            _modelTools.ListModelsDefinition(),
            _modelTools.ShowModelDefinition(),
            _skillTools.ListSkillsDefinition(),
            _skillTools.ShowSkillDefinition(),
            _orderTools.CreateOrderDefinition()
        ];
    }
}
