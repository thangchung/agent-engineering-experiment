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
        CodeModeWorkflowGuard workflowGuard = new();

        // Deduplication: passing the same name via both toolNames and name must return one result.
        SchemaLookupResponse response = CodeModeHandlers.get_schema(
            discoveryTools,
            workflowGuard,
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
        CodeModeWorkflowGuard workflowGuard = new();

        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            CodeModeHandlers.get_schema(
                discoveryTools,
                workflowGuard,
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
        CodeModeWorkflowGuard workflowGuard = new();

        DiscoverySearchResponse response = CodeModeHandlers.search(
            discoveryTools,
            workflowGuard,
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
    public async Task Execute_RequiresSearchThenGetSchema()
    {
        CodeModeWorkflowGuard workflowGuard = new();
        ISandboxRunner runner = new StubRunner(new RunnerResult(42, 0));
        ExecuteTool executeTool = new(runner);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CodeModeHandlers.execute("result = 42", executeTool, workflowGuard, NullLoggerFactory.Instance, CancellationToken.None));

        Assert.Contains("search", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("get_schema", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Execute_AllowsWhenSearchAndGetSchemaWereCalled()
    {
        ToolRegistry registry = new([TestTools.Create("brewery_search", "Find breweries", "{\"type\":\"object\"}")]);
        DiscoveryTools discoveryTools = new(registry, new WeightedToolSearcher(registry));
        CodeModeWorkflowGuard workflowGuard = new();

        _ = CodeModeHandlers.search(
            discoveryTools,
            workflowGuard,
            new UserContext(),
            NullLoggerFactory.Instance,
            "brewery",
            detail: TestJson.Parse("\"brief\""),
            tags: null,
            limit: 10);

        _ = CodeModeHandlers.get_schema(
            discoveryTools,
            workflowGuard,
            new UserContext(),
            NullLoggerFactory.Instance,
            toolNames: ["brewery_search"],
            name: null,
            detail: TestJson.Parse("\"brief\""));

        ISandboxRunner runner = new StubRunner(new RunnerResult(42, 0));
        ExecuteTool executeTool = new(runner);

        object? result = await CodeModeHandlers.execute("result = 42", executeTool, workflowGuard, NullLoggerFactory.Instance, CancellationToken.None);

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
