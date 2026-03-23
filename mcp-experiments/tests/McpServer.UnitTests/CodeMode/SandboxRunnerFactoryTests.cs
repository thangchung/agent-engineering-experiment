using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using McpServer.CodeMode;

namespace McpServer.UnitTests.CodeMode;

public sealed class SandboxRunnerFactoryTests
{
    [Fact]
    public void Create_LocalRunnerSelectedByDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        ISandboxRunner runner = SandboxRunnerFactory.Create(configuration, NullLoggerFactory.Instance);

        Assert.IsType<LocalConstrainedRunner>(runner);
    }

    [Fact]
    public void Create_OpenSandboxRunnerRequiresDomain()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CodeMode:Runner"] = "opensandbox",
            })
            .Build();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            SandboxRunnerFactory.Create(configuration, NullLoggerFactory.Instance));

        Assert.Contains("OpenSandbox:Domain", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_InvokesPreflightThenExecutesCode()
    {
        bool preflightCalled = false;
        bool remoteCalled = false;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(1),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ =>
            {
                preflightCalled = true;
                return Task.CompletedTask;
            },
            (_, _) =>
            {
                remoteCalled = true;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        RunnerResult result = await runner.RunAsync(
            "result = 1",
            CancellationToken.None);

        Assert.True(preflightCalled);
        Assert.True(remoteCalled);
        Assert.Equal("remote-result", result.FinalValue);
        Assert.Equal(0, result.CallsExecuted);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_WhenPreflightTimesOut_FailsWithoutRetryingPreflight()
    {
        int preflightCalls = 0;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(1),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ =>
            {
                preflightCalls++;
                throw new TimeoutException("sandbox readiness timeout");
            });

        InvalidOperationException firstError = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            "result = 1",
            CancellationToken.None));

        InvalidOperationException secondError = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            "result = 1",
            CancellationToken.None));

        Assert.Equal(1, preflightCalls);
        Assert.Contains("OpenSandbox is unavailable", firstError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenSandbox is unavailable", secondError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_WhenPreflightUnavailable_DoesNotExecuteRemoteCode()
    {
        bool remoteCalled = false;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(1),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ => throw new TimeoutException("sandbox readiness timeout"),
            (_, _) =>
            {
                remoteCalled = true;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "result = 1",
                CancellationToken.None));

        Assert.False(remoteCalled, "Remote executor should not be called when sandbox preflight is unavailable.");
        Assert.Contains("OpenSandbox is unavailable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_WhenHealthyAndNoToolBridge_UsesRemoteExecutor()
    {
        bool remoteCalled = false;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(1),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ => Task.CompletedTask,
            (_, _) =>
            {
                remoteCalled = true;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        RunnerResult result = await runner.RunAsync(
            "result = 1",
            CancellationToken.None);

        Assert.True(remoteCalled);
        Assert.Equal("remote-result", result.FinalValue);
        Assert.Equal(0, result.CallsExecuted);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_WhenCodeContainsMetaToolUsage_Throws()
    {
        // OpenSandbox mode must remain isolated from tool-search meta-tools.
        bool remoteCalled = false;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(5),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ => Task.CompletedTask,
            (_, _) =>
            {
                remoteCalled = true;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(
                "return await call_tool(\"ping\", {})",
                CancellationToken.None));

        Assert.False(remoteCalled, "Meta-tool usage should not reach the remote OpenSandbox executor.");
        Assert.Contains("isolated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenSandboxRunner_RunAsync_WhenPurePythonCodeUsed_ExecutesInSandbox()
    {
        // Pure Python code should pass validation and execute remotely.
        bool remoteCalled = false;
        string? codeReceivedBySandbox = null;

        OpenSandboxRunner runner = new(
            new OpenSandboxRunnerOptions
            {
                Domain = "localhost:8080",
                Timeout = TimeSpan.FromSeconds(5),
                MaxToolCalls = 5,
            },
            NullLoggerFactory.Instance,
            _ => Task.CompletedTask,
            (code, _) =>
            {
                remoteCalled = true;
                codeReceivedBySandbox = code;
                return Task.FromResult(new RunnerResult("remote-result", 0));
            });

        RunnerResult result = await runner.RunAsync(
            """
            values = [1, 2, 3]
            result = sum(values)
            """,
            CancellationToken.None);

        Assert.True(remoteCalled, "Sandbox must receive pure Python code.");
        Assert.NotNull(codeReceivedBySandbox);
        Assert.Contains("result = sum(values)", codeReceivedBySandbox, StringComparison.Ordinal);
        Assert.Equal("remote-result", result.FinalValue);
        Assert.Equal(0, result.CallsExecuted);
    }
}
