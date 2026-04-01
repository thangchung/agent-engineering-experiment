using McpServer.Cli.Commands;

namespace McpServer.UnitTests.Cli;

public sealed class ToolsShowCommandTests
{
    [Fact]
    public void BuildSampleUsage_UsesToolNameAndSchemaDerivedArguments()
    {
        string schema = """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search term to match against names."
            },
            "per_page": {
              "type": "integer"
            },
            "page": {
              "type": "integer"
            }
          }
        }
        """;

        string sample = ToolsShowCommand.BuildSampleUsage(
            TestTools.Create("brewery_search", "Search Breweries", schema));

        Assert.Contains("--no-launch-profile", sample);
        Assert.Contains("--tool brewery_search", sample);
        Assert.Contains("\"query\":\"moon\"", sample);
        Assert.Contains("\"per_page\":5", sample);
        Assert.Contains("\"page\":1", sample);
    }
}