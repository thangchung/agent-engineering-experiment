using LoopRuntime.Agents;
using LoopRuntime.Agents.Fakes;
using LoopRuntime.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoopRuntime.Tests;

/// <summary>
/// Deterministic end-to-end tests using FakeChatClient + FakeSandbox.
/// No Docker. No real model. Always finish the same way.
/// </summary>
public sealed class LoopOrchestratorTests
{
    [Fact]
    public async Task FibonacciTask_CompletesWithinCap_WhenCodeEventuallyPasses()
    {
        var sandbox = new Cap3PassingSandbox();
        var tools = new McpLocalTools(sandbox);
        var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);
        var executor = new ExecutorAgent(new FakeChatClient(), checker, tools, NullLogger<ExecutorAgent>.Instance);

        var result = await executor.RunAsync("Write a Fibonacci program", maxIterations: 5, CancellationToken.None);

        Assert.True(result.Completed, "Expected loop to complete (checker passes).");
        Assert.InRange(result.Iterations, 1, 5);
        Assert.NotEmpty(result.Path);
        Assert.Contains("def fibonacci", result.FinalCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AlwaysFailingSandbox_StopsAtCap()
    {
        var sandbox = new AlwaysFailSandbox();
        var tools = new McpLocalTools(sandbox);
        var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);
        var executor = new ExecutorAgent(new FakeChatClient(), checker, tools, NullLogger<ExecutorAgent>.Instance);

        var result = await executor.RunAsync("fibonacci", maxIterations: 3, CancellationToken.None);

        Assert.False(result.Completed, "Should NOT complete when sandbox always fails.");
    }

    // ── fake sandboxes ───────────────────────────────────────────────────────

    private sealed class FakeSandbox : ICodeSandbox
    {
        public Task<RunResult> RunAsync(string code, CancellationToken cancellationToken)
        {
            // Pass only when the code has both the function body AND the main block
            var ok = code.Contains("def fibonacci", StringComparison.Ordinal)
                  && code.Contains("return result", StringComparison.Ordinal)
                  && code.Contains("for x in fibonacci", StringComparison.Ordinal);

            return Task.FromResult(ok
                ? new RunResult(0, "0\n1\n1\n2\n3\n5\n8\n", string.Empty, false)
                : new RunResult(1, string.Empty, "NameError: name 'fib' is not defined", false));
        }
    }

    private sealed class Cap3PassingSandbox : ICodeSandbox
    {
        private int _calls;

        public Task<RunResult> RunAsync(string code, CancellationToken cancellationToken)
        {
            _calls++;
            var ok = code.Contains("def fibonacci", StringComparison.Ordinal)
                  && code.Contains("return result", StringComparison.Ordinal)
                  && code.Contains("for x in fibonacci", StringComparison.Ordinal);

            // Pass on the third attempt to match FakeChatClient scripted fixes
            return Task.FromResult(_calls >= 3 || ok
                ? new RunResult(0, "0\n1\n1\n2\n3\n5\n8\n", string.Empty, false)
                : new RunResult(1, string.Empty, "NameError: name 'fib' is not defined", false));
        }
    }

    private sealed class AlwaysFailSandbox : ICodeSandbox
    {
        public Task<RunResult> RunAsync(string code, CancellationToken cancellationToken)
            => Task.FromResult(new RunResult(1, string.Empty, "SyntaxError: always fails", false));
    }
}
