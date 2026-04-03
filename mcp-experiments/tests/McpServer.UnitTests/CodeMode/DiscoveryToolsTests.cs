using System.Text.Json;
using McpServer.CodeMode;
using McpServer.Registry;
using McpServer.Search;

namespace McpServer.UnitTests.CodeMode;

public sealed class DiscoveryToolsTests
{
    [Fact]
    public void SchemaDetailLevel_DeserializesFromLowercaseJsonString()
    {
        SchemaDetailLevel detail = JsonSerializer.Deserialize<SchemaDetailLevel>("\"detailed\"");

        Assert.Equal(SchemaDetailLevel.Detailed, detail);
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery_search", "Find breweries", "{}"),
            TestTools.Create("brewery_status", "Brewery status", "{}"),
            TestTools.Create("brewery_meta", "Brewery metadata", "{}"),
        ]);
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        DiscoverySearchResponse response = tools.Search("brewery", new UserContext(), limit: 2);

        Assert.Equal(2, response.Results.Count);
    }

    [Fact]
    public void Search_DefaultDetailIsBrief()
    {
        ToolRegistry registry = new([TestTools.Create("known", "Known", "{\"type\":\"object\"}")] );
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        DiscoverySearchResponse response = tools.Search("known", new UserContext());

        Assert.Single(response.Results);
        Assert.Null(response.Results[0].Parameters);
    }

    [Fact]
    public void Search_TagsFilterWorks()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery_search", "Find breweries", "{}", tags: ["brewery", "query"]),
            TestTools.Create("weather_search", "Find weather", "{}", tags: ["weather"]),
        ]);
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        DiscoverySearchResponse response = tools.Search("Search", new UserContext(), tags: ["brewery"]);

        Assert.Single(response.Results);
        Assert.Equal("brewery_search", response.Results[0].Name);
    }

    [Fact]
    public void Search_SubsetAnnotationWhenTruncated()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create("brewery_search", "Find breweries", "{}"),
            TestTools.Create("brewery_status", "Brewery status", "{}"),
            TestTools.Create("brewery_meta", "Brewery metadata", "{}"),
        ]);
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        DiscoverySearchResponse response = tools.Search("brewery", new UserContext(), detail: SchemaDetailLevel.Detailed, limit: 1);

        Assert.Equal(3, response.TotalMatched);
        Assert.Equal("1 of 3 tools", response.Annotation);
    }

    [Fact]
    public void GetSchema_DefaultDetailIsDetailedMarkdown()
    {
        ToolRegistry registry = new([TestTools.Create("known", "Known", "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"}}}")] );
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        SchemaLookupResponse response = tools.GetSchema(["known"], new UserContext());

        Assert.Single(response.Results);
        Assert.Contains("- x (type: integer, optional)", response.Results[0].Schema, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchema_FullDetailReturnsJsonSchema()
    {
        ToolRegistry registry = new([TestTools.Create("known", "Known", "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"integer\"}}}")] );
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        SchemaLookupResponse response = tools.GetSchema(["known"], new UserContext(), SchemaDetailLevel.Full);

        Assert.Single(response.Results);
        Assert.Contains("\"properties\"", response.Results[0].Schema, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSchema_ReportsMissingAlongsideMatched()
    {
        ToolRegistry registry = new([TestTools.Create("known", "Known", "{\"type\":\"object\"}")] );
        DiscoveryTools tools = new(registry, new WeightedToolSearcher(registry));

        SchemaLookupResponse response = tools.GetSchema(["known", "missing"], new UserContext(), SchemaDetailLevel.Detailed);

        Assert.Single(response.Results);
        Assert.Single(response.Missing);
        Assert.Equal("missing", response.Missing[0]);
    }
}
