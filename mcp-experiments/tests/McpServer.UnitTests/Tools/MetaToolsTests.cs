using McpServer.Registry;
using McpServer.Search;
using McpServer.Tools;

namespace McpServer.UnitTests.Tools;

public sealed class MetaToolsTests
{
    [Fact]
    public void SearchTools_ReturnsSchemas()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}"),
        ]);
        WeightedToolSearcher searcher = new(registry);
        MetaTools metaTools = new(registry, searcher);

        IReadOnlyList<ToolDefinition> results = metaTools.SearchTools("brewery", 10, new UserContext());

        Assert.Single(results);
        Assert.False(string.IsNullOrWhiteSpace(results[0].InputJsonSchema));
    }

    [Fact]
    public async Task CallTool_RejectsSyntheticRecursion()
    {
        ToolRegistry registry = new([TestTools.Create("real", "Real", "{}")]);
        WeightedToolSearcher searcher = new(registry);
        MetaTools metaTools = new(registry, searcher);

        await Assert.ThrowsAsync<SyntheticToolRecursionException>(() =>
            metaTools.CallToolAsync("call_tool", TestTools.EmptyArgs(), new UserContext(), CancellationToken.None));
    }
}
