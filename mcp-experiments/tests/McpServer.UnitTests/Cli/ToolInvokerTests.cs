using McpServer.Cli;
using McpServer.Registry;
using McpServer.Search;
using McpServer.Tools;

namespace McpServer.UnitTests.Cli;

public sealed class ToolInvokerTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsSuccessResult()
    {
        ToolRegistry registry = new([TestTools.Create("status", "desc", "{}")]);
        WeightedToolSearcher searcher = new(registry);
        MetaTools metaTools = new(registry, searcher);
        ToolInvoker invoker = new(metaTools, new UserContext());

        ToolInvocationResult result = await invoker.InvokeAsync("status", TestTools.EmptyArgs(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("status", result.ToolName);
    }
}
