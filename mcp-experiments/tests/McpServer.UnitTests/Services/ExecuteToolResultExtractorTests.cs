using System.Text.Json;
using McpServer.Services;

namespace McpServer.UnitTests.Services;

public sealed class ExecuteToolResultExtractorTests
{
    [Fact]
    public void TryExtractJsonObject_ReturnsRootObject_WhenTextIsJson()
    {
        const string input = "{" +
            "\"result\":{\"san_diego_total\":91,\"san_diego_type_summary\":[],\"san_diego_top_cities\":[],\"moon_top_5\":[]}," +
            "\"errors\":[]}";

        bool success = ExecuteToolResultExtractor.TryExtractJsonObject(input, out JsonElement result);

        Assert.True(success);
        Assert.True(result.TryGetProperty("result", out _));
    }

    [Fact]
    public void TryExtractJsonObject_ParsesJsonInsideCodeFence()
    {
        const string input = "```json\n{\n  \"result\": {\n    \"san_diego_total\": 91,\n    \"san_diego_type_summary\": [],\n    \"san_diego_top_cities\": [],\n    \"moon_top_5\": []\n  },\n  \"errors\": []\n}\n```";

        bool success = ExecuteToolResultExtractor.TryExtractJsonObject(input, out JsonElement result);

        Assert.True(success);
        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public void TryExtractJsonObject_ParsesEmbeddedJsonObject()
    {
        const string input = "Here are the results:\n{\"result\":{\"san_diego_total\":91,\"san_diego_type_summary\":[],\"san_diego_top_cities\":[],\"moon_top_5\":[]},\"errors\":[]}\nDone.";

        bool success = ExecuteToolResultExtractor.TryExtractJsonObject(input, out JsonElement result);

        Assert.True(success);
        Assert.True(result.TryGetProperty("errors", out _));
    }

    [Fact]
    public void TryExtractJsonObject_ParsesJsonFromFencedBlockInsideProse()
    {
        const string input = "Execution completed.\n```json\n{\"result\":{\"count\":2},\"errors\":[]}\n```\nReturning summary.";

        bool success = ExecuteToolResultExtractor.TryExtractJsonObject(input, out JsonElement result);

        Assert.True(success);
        Assert.True(result.TryGetProperty("result", out JsonElement payload));
        Assert.Equal(2, payload.GetProperty("count").GetInt32());
    }

    [Fact]
    public void TryExtractJsonObject_PrefersRootPayloadOverNestedObjects()
    {
        const string input =
            "Row preview: {\"name\":\"10 Barrel Brewing Co\",\"type\":\"large\"}. " +
            "Final output: {\"result\":[{\"name\":\"10 Barrel Brewing Co\",\"type\":\"large\"},{\"name\":\"2Kids Brewing Company\",\"type\":\"micro\"}],\"errors\":[]}";

        bool success = ExecuteToolResultExtractor.TryExtractJsonObject(input, out JsonElement result);

        Assert.True(success);
        Assert.True(result.TryGetProperty("result", out JsonElement payload));
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
        Assert.Equal(2, payload.GetArrayLength());
    }
}