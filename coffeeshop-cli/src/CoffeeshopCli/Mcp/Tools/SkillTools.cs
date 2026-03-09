using CoffeeshopCli.Services;

namespace CoffeeshopCli.Mcp.Tools;

/// <summary>
/// Skill-related tool definitions and operations.
/// </summary>
public sealed class SkillTools
{
    private readonly IDiscoveryService _discovery;

    public SkillTools(IDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    public object ListSkillsDefinition() => new
    {
        name = "list_skills",
        description = "List available skills",
        annotations = new { readOnlyHint = true }
    };

    public object ShowSkillDefinition() => new
    {
        name = "show_skill",
        description = "Show skill manifest",
        annotations = new { readOnlyHint = true }
    };
}
