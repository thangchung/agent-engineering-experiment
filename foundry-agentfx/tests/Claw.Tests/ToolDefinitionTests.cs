using Claw.Core;

namespace Claw.Tests;

public class ToolDefinitionTests
{
    [Fact]
    public void ToolDefinition_Record_Equality()
    {
        var a = new ToolDefinition("my_tool", "desc", "{}", [], false, false);
        var b = new ToolDefinition("my_tool", "desc", "{}", [], false, false);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ToolDefinition_Different_Name_NotEqual()
    {
        var a = new ToolDefinition("tool_a", "desc", "{}", [], false, false);
        var b = new ToolDefinition("tool_b", "desc", "{}", [], false, false);
        Assert.NotEqual(a, b);
    }
}
