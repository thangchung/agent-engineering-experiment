using Microsoft.Extensions.Logging.Abstractions;
using McpServer.CodeMode;
using McpServer.Registry;
using System.Text.Json;

namespace McpServer.UnitTests.CodeMode;

public sealed class ExecuteAndRunnerTests
{
    [Fact]
    public async Task LocalConstrainedRunner_RejectsMetaToolUsage()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromSeconds(2), maxToolCalls: 10, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            result = call_tool("brewery_search", {"query": "moon"})
            """;

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(code, CancellationToken.None));

        Assert.Contains("isolated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalConstrainedRunner_TimeoutStopsExecution()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromMilliseconds(50), maxToolCalls: 10, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            import time
            time.sleep(0.2)
            result = 1
            """;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(code, CancellationToken.None));
    }

    [Fact]
    public async Task LocalConstrainedRunner_MultilinePythonExecutesSuccessfully()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromSeconds(5), maxToolCalls: 10, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            import json
            data = [{"name": "Moon", "city": "San Diego"}, {"name": "Sun", "city": "LA"}]
            result = [{"name": x["name"], "city": x["city"]} for x in data]
            """;

        RunnerResult result = await runner.RunAsync(code, CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(result.FinalValue);
        Assert.Equal(JsonValueKind.Array, final.ValueKind);
        Assert.Equal(2, final.GetArrayLength());
        Assert.Equal("Moon", final[0].GetProperty("name").GetString());
        Assert.Equal(0, result.CallsExecuted);
    }

    [Fact]
    public async Task LocalConstrainedRunner_ProvidesRequestsCompatibilityModule()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromSeconds(5), maxToolCalls: 10, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            import requests
            result = {
                "has_get": hasattr(requests, "get"),
                "has_request": hasattr(requests, "request")
            }
            """;

        RunnerResult result = await runner.RunAsync(code, CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(result.FinalValue);
        Assert.True(final.GetProperty("has_get").GetBoolean());
        Assert.True(final.GetProperty("has_request").GetBoolean());
    }

    [Fact]
    public async Task LocalConstrainedRunner_WhenHttp403Occurs_ReturnsHelpfulHint()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromSeconds(5), maxToolCalls: 10, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            raise Exception("HTTP Error 403: Forbidden")
            """;

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(code, CancellationToken.None));

        Assert.Contains("HTTP 403", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User-Agent", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteTool_ReturnsRunnerFinalValueOnly()
    {
        ISandboxRunner runner = new StubRunner(new RunnerResult(new { total = 42 }, 0));
        ExecuteTool executeTool = new(runner);

        ExecuteResponse response = await executeTool.ExecuteAsync("result = 42", CancellationToken.None);

        Assert.NotNull(response.FinalValue);
    }

    [Fact]
    public async Task WorkflowCoordinator_DelegatesToExecuteTool()
    {
        ISandboxRunner runner = new StubRunner(new RunnerResult("done", 0));
        ExecuteTool executeTool = new(runner);
        WorkflowCoordinator coordinator = new(executeTool);

        object? result = await coordinator.RunAsync("result = 'done'", new UserContext(), CancellationToken.None);

        Assert.Equal("done", result);
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
