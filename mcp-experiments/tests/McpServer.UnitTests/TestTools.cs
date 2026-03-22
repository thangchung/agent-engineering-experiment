using System.Text.Json;
using McpServer.Registry;

namespace McpServer.UnitTests;

internal static class TestTools
{
    public static ToolDescriptor Create(
        string name,
        string description,
        string schema,
        IReadOnlyList<string>? tags = null,
        bool isPinned = false,
        bool isSynthetic = false,
        Func<UserContext, bool>? isVisible = null,
        ToolHandler? handler = null)
    {
        return new ToolDescriptor(
            name,
            description,
            schema,
            tags ?? [],
            isPinned,
            isSynthetic,
            isVisible ?? (_ => true),
            handler ?? ((_, _) => Task.FromResult<object?>(name)));
    }

    public static JsonElement EmptyArgs()
    {
        return TestJson.Parse("{}");
    }
}
