using McpServer.Registry;
using McpServer.Search;

namespace McpServer.UnitTests.Search;

public sealed class WeightedToolSearcherTests
{
    [Fact]
    public void ExactNameWinsOverDescription()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery_search", "Generic lookup", "{}"),
            TestTools.Create("lookup", "Can brewery Search by city", "{}"),
        ]);

        WeightedToolSearcher searcher = new(registry);

        IReadOnlyList<ToolDescriptor> results = searcher.Search("brewery_search", 10, new UserContext());

        Assert.Equal("brewery_search", results[0].Name);
    }

    [Fact]
    public void TieKeepsCatalogOrder()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("first", "Find city", "{}", tags: ["city"]),
            TestTools.Create("second", "Find city", "{}", tags: ["city"]),
        ]);

        WeightedToolSearcher searcher = new(registry);

        IReadOnlyList<ToolDescriptor> results = searcher.Search("city", 10, new UserContext());

        Assert.Equal(["first", "second"], results.Select(item => item.Name).ToArray());
    }

    [Fact]
    public void ZeroScoreExcluded()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("alpha", "No overlap", "{}"),
            TestTools.Create("beta", "Still no overlap", "{}"),
        ]);

        WeightedToolSearcher searcher = new(registry);

        IReadOnlyList<ToolDescriptor> results = searcher.Search("brewery", 10, new UserContext());

        Assert.Empty(results);
    }

    [Fact]
    public void NameMatchOutranksDescriptionMatch()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery", "Simple tool", "{}"),
            TestTools.Create("find", "This tool can brewery lookup", "{}"),
        ]);

        WeightedToolSearcher searcher = new(registry);

        IReadOnlyList<ToolDescriptor> results = searcher.Search("brewery", 10, new UserContext());

        Assert.Equal("brewery", results[0].Name);
    }
}
