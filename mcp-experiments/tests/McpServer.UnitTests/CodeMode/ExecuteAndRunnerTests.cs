using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using McpServer.CodeMode;
using McpServer.Registry;

namespace McpServer.UnitTests.CodeMode;

public sealed class ExecuteAndRunnerTests
{
    [Fact]
    public async Task LocalConstrainedRunner_MaxToolCallsEnforced()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromSeconds(2), maxToolCalls: 1, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            a = await call_tool("one", {})
            return await call_tool("two", {})
            """;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(code, (_, _, _) => Task.FromResult<object?>("ok"), CancellationToken.None));
    }

    [Fact]
    public async Task LocalConstrainedRunner_TimeoutStopsExecution()
    {
        LocalConstrainedRunner runner = new(TimeSpan.FromMilliseconds(50), maxToolCalls: 3, NullLogger<LocalConstrainedRunner>.Instance);
        string code = """
            return await call_tool("slow", {})
            """;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                code,
                async (_, _, ct) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                    return "never";
                },
                CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteTool_CodeChainReturnsFinalValueOnly()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create(
                "double",
                "Double",
                "{}",
                handler: (args, _) =>
                {
                    int x = args.TryGetProperty("x", out JsonElement xElement) ? xElement.GetInt32() : 0;
                    return Task.FromResult<object?>(TestJson.Parse($"{{\"result\":{x * 2}}}"));
                }),
            TestTools.Create(
                "add_one",
                "Add one",
                "{}",
                handler: (args, _) =>
                {
                    int x = args.TryGetProperty("x", out JsonElement xElement) ? xElement.GetInt32() : 0;
                    return Task.FromResult<object?>(TestJson.Parse($"{{\"result\":{x + 1}}}"));
                }),
        ]);

        ExecuteTool executeTool = new(registry, new LocalConstrainedRunner(TimeSpan.FromSeconds(2), 10, NullLogger<LocalConstrainedRunner>.Instance));
        string code = """
            a = await call_tool("double", {"x": 3})
            b = await call_tool("add_one", {"x": a["result"]})
            return b
            """;

        ExecuteResponse response = await executeTool.ExecuteAsync(code, new UserContext(), CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(response.FinalValue);
        Assert.Equal(7, final.GetProperty("result").GetInt32());
    }

    [Fact]
    public async Task ExecuteTool_ReturnExpressionCanCallTool()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create(
                "ping",
                "Ping",
                "{}",
                handler: (_, _) => Task.FromResult<object?>(TestJson.Parse("{\"result\":\"pong\"}"))),
        ]);

        ExecuteTool executeTool = new(registry, new LocalConstrainedRunner(TimeSpan.FromSeconds(2), 10, NullLogger<LocalConstrainedRunner>.Instance));
        string code = """
            return await call_tool("ping", {})
            """;

        ExecuteResponse response = await executeTool.ExecuteAsync(code, new UserContext(), CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(response.FinalValue);
        Assert.Equal("pong", final.GetProperty("result").GetString());
    }

    [Fact]
    public async Task WorkflowCoordinator_DelegatesToExecuteTool()
    {
        ToolRegistry registry = new(
        [
            TestTools.Create(
                "single",
                "Single",
                "{}",
                handler: (_, _) => Task.FromResult<object?>(TestJson.Parse("{\"result\":\"done\"}"))),
        ]);

        ExecuteTool executeTool = new(registry, new LocalConstrainedRunner(TimeSpan.FromSeconds(2), 10, NullLogger<LocalConstrainedRunner>.Instance));
        WorkflowCoordinator coordinator = new(executeTool);

        object? result = await coordinator.RunAsync(
            "return await call_tool(\"single\", {})",
            new UserContext(),
            CancellationToken.None);

        JsonElement final = Assert.IsType<JsonElement>(result);
        Assert.Equal("done", final.GetProperty("result").GetString());
    }
}
