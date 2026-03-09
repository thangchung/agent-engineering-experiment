using CoffeeshopCli.Mcp.Tools;
using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Mcp;

public sealed class McpToolRegistryTests
{
    [Fact]
    public void ListTools_ExposesAtLeastFiveTools()
    {
        var discovery = new FileSystemDiscoveryService(new ModelRegistry(), "./skills");
        var registry = new ToolRegistry(
            new ModelTools(discovery),
            new SkillTools(discovery),
            new OrderTools()
        );

        var tools = registry.ListTools();

        Assert.True(tools.Count >= 5);
    }

    [Fact]
    public void ReadOnlyTools_IncludeReadOnlyHint()
    {
        var discovery = new FileSystemDiscoveryService(new ModelRegistry(), "./skills");
        var modelTools = new ModelTools(discovery);
        var skillTools = new SkillTools(discovery);

        var defs = new[]
        {
            modelTools.ListModelsDefinition(),
            modelTools.ShowModelDefinition(),
            skillTools.ListSkillsDefinition(),
            skillTools.ShowSkillDefinition()
        };

        foreach (var def in defs)
        {
            var props = def.GetType().GetProperties();
            var annotationsProp = props.FirstOrDefault(p => p.Name == "annotations" || p.Name == "Annotations");
            Assert.NotNull(annotationsProp);
            var annotationsVal = annotationsProp!.GetValue(def);
            Assert.NotNull(annotationsVal);
            var roProp = annotationsVal!.GetType().GetProperties().FirstOrDefault(p => p.Name == "readOnlyHint" || p.Name == "ReadOnlyHint");
            Assert.NotNull(roProp);
            var roValue = roProp!.GetValue(annotationsVal);
            Assert.Equal(true, roValue);
        }
    }
}
