using LoopRuntime.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LoopRuntime.Agents;

/// <summary>
/// IChecker impl that uses an LLM via <see cref="IChatClient"/> to review code.
/// Runs the file in the sandbox, then asks the model for a pass/fail verdict
/// and structured fix hints.
/// </summary>
public sealed class CheckerAgent : IChecker
{
    private readonly IChatClient _chatClient;
    private readonly IMcpTools _tools;
    private readonly ILogger<CheckerAgent> _logger;

    public CheckerAgent(IChatClient chatClient, IMcpTools tools, ILogger<CheckerAgent> logger)
    {
        _chatClient = chatClient;
        _tools = tools;
        _logger = logger;
    }

    public async Task<Verdict> ReviewAsync(string sessionId, string path, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Review started. SessionId={SessionId} Path={Path}", sessionId, path);
        var run = await _tools.RunPythonAsync(path, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Review result. SessionId={SessionId} ExitCode={ExitCode} TimedOut={TimedOut} StdoutLength={StdoutLength} StderrLength={StderrLength}",
            sessionId, run.ExitCode, run.TimedOut, run.Stdout.Length, run.Stderr.Length);

        if (!run.TimedOut && run.ExitCode == 0)
        {
            return new Verdict(true, null, run);
        }

        var reason = run.TimedOut
            ? "Execution timed out after 10s."
            : await AskModelForReasonAsync(path, run, cancellationToken).ConfigureAwait(false);

        var fixes = SuggestFixes(reason);
        _logger.LogWarning("Review failed. SessionId={SessionId} Reason={Reason}", sessionId, reason);
        return new Verdict(false, new Feedback(reason, fixes), run);
    }

    private async Task<string> AskModelForReasonAsync(string path, RunResult run, CancellationToken cancellationToken)
    {
        // The injected IChatClient is already a TaggingChatClient registered by
        // AddChatClient, so we use it directly to avoid duplicate GenAI spans.
        var agent = _chatClient.AsAIAgent(
            instructions: """
                You are a Python code reviewer. Given a file path and execution output,
                return a single concise sentence describing the first error.
                Do not return JSON or bullet lists.
                """,
            name: "checker",
            description: "Python code reviewer",
            loggerFactory: NullLoggerFactory.Instance,
            services: new ServiceCollection().BuildServiceProvider());

        var prompt = $"""
            Path: {path}
            Exit code: {run.ExitCode}
            Stderr:
            {run.Stderr}

            What is the first error?
            """;

        var response = await agent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            options: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var text = response.Text?.Trim();
        return string.IsNullOrWhiteSpace(text) ? FirstReasonFallback(run) : text;
    }

    private static string FirstReasonFallback(RunResult run)
    {
        if (run.TimedOut)
        {
            return "Execution timed out after 10s.";
        }

        var line = run.Stderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static x => x.Trim())
            .FirstOrDefault(static x => x.Length > 0);

        return string.IsNullOrWhiteSpace(line) ? $"Execution failed with exit code {run.ExitCode}." : line;
    }

    private static IReadOnlyList<string> SuggestFixes(string reason)
    {
        if (reason.Contains("NameError", StringComparison.OrdinalIgnoreCase))
        {
            return ["Use correct function and variable names.", "Define symbol before use."];
        }

        if (reason.Contains("IndentationError", StringComparison.OrdinalIgnoreCase))
        {
            return ["Fix indentation blocks.", "Keep return statement in intended scope."];
        }

        if (reason.Contains("SyntaxError", StringComparison.OrdinalIgnoreCase))
        {
            return ["Fix Python syntax near reported line.", "Run linter locally before submit."];
        }

        return ["Read traceback and fix first failing line."];
    }
}
