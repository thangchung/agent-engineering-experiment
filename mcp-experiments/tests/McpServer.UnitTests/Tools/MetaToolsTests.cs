using McpServer.CodeMode;
using McpServer.Registry;
using McpServer.Search;
using McpServer.Tools;
using Microsoft.Extensions.Logging.Abstractions;

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

    [Fact]
    public void GetSchema_AcceptsListAndSingleNameParameters()
    {
        ToolRegistry registry = new([TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}")]);
        DiscoveryTools discoveryTools = new(registry, new WeightedToolSearcher(registry));

        // Deduplication: passing the same name via both toolNames and name must return one result.
        SchemaLookupResponse response = McpToolHandlers.get_schema(
            discoveryTools,
            new UserContext(),
            toolNames: ["brewery_search"],
            name: "brewery_search",
            detail: TestJson.Parse("\"detailed\""));

        Assert.Single(response.Results);
        Assert.Empty(response.Missing);
        Assert.Equal("brewery_search", response.Results[0].Name);
    }

    [Fact]
    public void GetSchema_ThrowsClearErrorWhenNoNameProvided()
    {
        ToolRegistry registry = new([TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}")]);
        DiscoveryTools discoveryTools = new(registry, new WeightedToolSearcher(registry));

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            McpToolHandlers.get_schema(
                discoveryTools,
                new UserContext(),
                toolNames: null,
                name: null,
                detail: TestJson.Parse("\"detailed\"")));

        Assert.Contains("Provide at least one tool name", ex.Message, StringComparison.Ordinal);
        Assert.Equal("arguments", ex.ParamName);
    }

    [Fact]
    public void Search_AcceptsLowercaseStringDetail()
    {
        ToolRegistry registry = new([TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}")]);
        DiscoveryTools discoveryTools = new(registry, new WeightedToolSearcher(registry));

        DiscoverySearchResponse response = McpToolHandlers.search(
            discoveryTools,
            new UserContext(),
            NullLoggerFactory.Instance,
            "brewery",
            detail: TestJson.Parse("\"detailed\""),
            tags: null,
            limit: 10);

        Assert.Single(response.Results);
        Assert.NotNull(response.Results[0].Parameters);
    }
}
