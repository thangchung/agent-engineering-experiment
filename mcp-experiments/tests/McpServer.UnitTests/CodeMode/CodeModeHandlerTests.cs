using McpServer.CodeMode;
using McpServer.Registry;
using McpServer.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace McpServer.UnitTests.CodeMode;

public sealed class CodeModeHandlerTests
{
    [Fact]
    public void GetSchema_AcceptsListAndSingleNameParameters()
    {
        ToolRegistry registry = new([TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}")]);
        DiscoveryTools discoveryTools = new(registry, new WeightedToolSearcher(registry));

        // Deduplication: passing the same name via both toolNames and name must return one result.
        SchemaLookupResponse response = CodeModeHandlers.GetSchema(
            discoveryTools,
            new UserContext(),
            NullLoggerFactory.Instance,
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
            CodeModeHandlers.GetSchema(
                discoveryTools,
                new UserContext(),
                NullLoggerFactory.Instance,
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

        DiscoverySearchResponse response = CodeModeHandlers.Search(
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

    [Fact]
    public async Task Execute_ReturnsRunnerResult()
    {
        ISandboxRunner runner = new StubRunner(new RunnerResult(42, 0));
        ExecuteTool executeTool = new(runner);

        object? result = await CodeModeHandlers.Execute("result = 42", executeTool, NullLoggerFactory.Instance, CancellationToken.None);

        int final = Assert.IsType<int>(result);
        Assert.Equal(42, final);
    }

    private sealed class StubRunner(RunnerResult nextResult) : ISandboxRunner
    {
        public string SyntaxGuide => "stub";

        public Task<RunnerResult> RunAsync(string code, CancellationToken ct)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            return Task.FromResult(nextResult);
        }
    }
}
