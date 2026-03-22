using McpServer.Registry;
using McpServer.Search;
using McpServer.Tools;

namespace McpServer.UnitTests.Tools;

public sealed class MetaToolsTests
{
    [Fact]
    public void ListTools_OnlySyntheticAndPinned()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("real", "Real", "{}", isPinned: false, isSynthetic: false),
            TestTools.Create("pinned", "Pinned", "{}", isPinned: true, isSynthetic: false),
            TestTools.Create("search_tools", "Synthetic", "{}", isPinned: false, isSynthetic: true),
        ]);
        WeightedToolSearcher searcher = new(registry);
        MetaTools metaTools = new(registry, searcher);

        IReadOnlyList<ToolDefinition> list = metaTools.ListTools(new UserContext());

        Assert.Equal(["pinned", "search_tools"], list.Select(item => item.Name).ToArray());
    }

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
