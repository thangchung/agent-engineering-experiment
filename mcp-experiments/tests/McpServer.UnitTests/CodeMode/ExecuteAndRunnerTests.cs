using Microsoft.Extensions.Logging.Abstractions;
using McpServer.CodeMode;
using McpServer.CodeMode.Local;
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
    public void LocalConstrainedRunner_SyntaxGuide_IncludesBaseUrlGuidance()
    {
        LocalConstrainedRunner runner = new(
            TimeSpan.FromSeconds(5),
            maxToolCalls: 10,
            NullLogger<LocalConstrainedRunner>.Instance,
            ["https://petstore3.swagger.io/api/v3"]);

        Assert.Contains("BASE_URL", runner.SyntaxGuide, StringComparison.Ordinal);
        Assert.Contains("petstore3.swagger.io", runner.SyntaxGuide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocalConstrainedRunner_ExposesConfiguredBaseUrlToPythonScope()
    {
        LocalConstrainedRunner runner = new(
            TimeSpan.FromSeconds(5),
            maxToolCalls: 10,
            NullLogger<LocalConstrainedRunner>.Instance,
            ["https://example.test/api"]);

        RunnerResult result = await runner.RunAsync("result = BASE_URL", CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(result.FinalValue);
        Assert.Equal("https://example.test/api", final.GetString());
    }

    [Fact]
    public async Task LocalConstrainedRunner_RejectsHardcodedUrlOutsideConfiguredDataSources()
    {
        LocalConstrainedRunner runner = new(
            TimeSpan.FromSeconds(5),
            maxToolCalls: 10,
            NullLogger<LocalConstrainedRunner>.Instance,
            ["https://petstore3.swagger.io/api/v3"]);

        string code = """
            import requests
            response = requests.get("https://petstore.swagger.io/v2/pet/findByStatus", params={"status": "sold"})
            result = response.json()
            """;

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(code, CancellationToken.None));

        Assert.Contains("configured OpenAPI data sources", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BASE_URL", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LocalConstrainedRunner_AllowsHardcodedUrlWithinConfiguredDataSources()
    {
        LocalConstrainedRunner runner = new(
            TimeSpan.FromSeconds(5),
            maxToolCalls: 10,
            NullLogger<LocalConstrainedRunner>.Instance,
            ["https://petstore3.swagger.io/api/v3"]);

        string code = """
            result = "https://petstore3.swagger.io/api/v3/pet/findByStatus"
            """;

        RunnerResult result = await runner.RunAsync(code, CancellationToken.None);
        JsonElement final = Assert.IsType<JsonElement>(result.FinalValue);
        Assert.Equal("https://petstore3.swagger.io/api/v3/pet/findByStatus", final.GetString());
    }

    [Fact]
    public async Task ExecuteTool_ReturnsRunnerFinalValueOnly()
    {
        ISandboxRunner runner = new StubRunner(new RunnerResult(new { total = 42 }, 0));
        ExecuteTool executeTool = new(runner);

        ExecuteResponse response = await executeTool.ExecuteAsync("result = 42", CancellationToken.None);

        Assert.NotNull(response.FinalValue);
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
