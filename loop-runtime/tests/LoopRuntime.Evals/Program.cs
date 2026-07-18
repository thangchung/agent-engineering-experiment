// LoopRuntime.Evals — P2 golden-task pass-rate baseline.
// Uses FakeChatClient + FakeSandbox so results are deterministic.
// Swap FakeChatClient for real IChatClient to get a live eval.
using LoopRuntime.Agents;
using LoopRuntime.Agents.Fakes;
using LoopRuntime.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

var tasks = new[]
{
    "Write a Fibonacci program that prints the first 20 numbers",
    "Create a fibonacci sequence printer",
};

int passed = 0;
foreach (var task in tasks)
{
    var sandbox = new EvalSandbox();
    var tools = new McpLocalTools(sandbox);
    var checker = new CheckerAgent(new FakeChatClient(), tools, NullLogger<CheckerAgent>.Instance);
    var executor = new ExecutorAgent(new FakeChatClient(), checker, tools, NullLogger<ExecutorAgent>.Instance);

    var result = await executor.RunAsync(task, maxIterations: 5, CancellationToken.None);
    if (result.Completed)
    {
        passed++;
    }

    Console.WriteLine($"[{(result.Completed ? "PASS" : "FAIL")}] {task} — iter={result.Iterations} path={result.Path}");
}

var rate = tasks.Length == 0 ? 0.0 : (double)passed / tasks.Length;
Console.WriteLine($"\nEval pass rate: {passed}/{tasks.Length} ({rate:P0})");

// ── deterministic sandbox used by eval harness ───────────────────────────────

internal sealed class EvalSandbox : ICodeSandbox
{
    public Task<RunResult> RunAsync(string code, CancellationToken cancellationToken)
    {
        var ok = code.Contains("def fibonacci", StringComparison.Ordinal)
              && code.Contains("return result", StringComparison.Ordinal)
              && code.Contains("for x in fibonacci", StringComparison.Ordinal);

        return Task.FromResult(ok
            ? new RunResult(0, "0 1 1 2 3 5 8 13 21 34 55 89 144 233 377 610 987 1597 2584 4181", string.Empty, false)
            : new RunResult(1, string.Empty, "NameError: name 'fib' is not defined", false));
	}
}
