using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LoopRuntime.Contracts;

namespace LoopRuntime.Agents;

/// <summary>
/// Wires the MAF LoopAgent (Harness/Loop) over an IChatClient + IChecker.
///
/// Loop contract (per plan):
///   Executor = IChatClient.AsAIAgent(tools: [write_file, edit_file])
///   Checker  = DelegateLoopEvaluator → IChecker.ReviewAsync → LoopEvaluation.Stop/Continue
///   Loop     = new LoopAgent(executor, checker, MaxIterations=5)
///
/// Session ID: generated once, bound into tool closures and the evaluator.
/// Path tracking: write_file / edit_file capture the returned path into a closure variable.
/// Stall guard (P2): same-hash detection — Stop with reason="stalled".
/// </summary>
public sealed class ExecutorAgent
{
    private readonly IChatClient _chatClient;
    private readonly IChecker _checker;
    private readonly IMcpTools _tools;
    private readonly ILogger<ExecutorAgent> _logger;

    public ExecutorAgent(IChatClient chatClient, IChecker checker, IMcpTools tools, ILogger<ExecutorAgent> logger)
    {
        _chatClient = chatClient;
        _checker = checker;
        _tools = tools;
        _logger = logger;
    }

    public async Task<LoopRunResult> RunAsync(
        string prompt,
        int maxIterations = 5,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var fileName = BuildFileName(prompt);

        _logger.LogInformation("Starting loop run. SessionId={SessionId} PromptLength={PromptLength} MaxIterations={MaxIterations}", sessionId, prompt.Length, maxIterations);

        // Mutable path state shared between tool closures and the evaluator.
        string? lastPath = null;
        string? lastCodeHash = null; // stall detection (P2)
        int finalIteration = 0;

        // --- tool functions bound to this session ---
        var writeFileTool = AIFunctionFactory.Create(
            async ([System.ComponentModel.Description("Complete Python source code to write.")] string content, CancellationToken ct) =>
            {
                _logger.LogInformation("write_file called. SessionId={SessionId} Name={Name} ContentLength={ContentLength}", sessionId, fileName, content.Length);
                lastPath = await _tools.WriteFileAsync(sessionId, fileName, content, ct).ConfigureAwait(false);
                lastCodeHash = ComputeHash(content);
                _logger.LogInformation("write_file completed. SessionId={SessionId} Path={Path}", sessionId, lastPath);
                return lastPath;
            },
            "write_file",
            "Write Python code to a new file. Call on the first attempt. Returns the file path.");

        var editFileTool = AIFunctionFactory.Create(
            async ([System.ComponentModel.Description("Revised Python source code.")] string content, CancellationToken ct) =>
            {
                _logger.LogInformation("edit_file called. SessionId={SessionId} ContentLength={ContentLength}", sessionId, content.Length);
                if (lastPath is null)
                {
                    _logger.LogWarning("edit_file called before write_file; creating file. SessionId={SessionId}", sessionId);
                    lastPath = await _tools.WriteFileAsync(sessionId, fileName, content, ct).ConfigureAwait(false);
                }
                else
                {
                    lastPath = await _tools.EditFileAsync(sessionId, lastPath, content, ct).ConfigureAwait(false);
                }

                // Stall detection: same hash twice → stop
                var newHash = ComputeHash(content);
                if (lastCodeHash == newHash)
                {
                    _logger.LogWarning("Stall detected: code hash unchanged. SessionId={SessionId}", sessionId);
                    lastCodeHash = "__stalled__";
                }
                else
                {
                    lastCodeHash = newHash;
                }

                _logger.LogInformation("edit_file completed. SessionId={SessionId} Path={Path}", sessionId, lastPath);
                return lastPath;
            },
            "edit_file",
            "Edit the existing Python file with revised code. Returns the file path.");

        // --- MAF LoopAgent executor (wraps IChatClient) ---
        // The injected IChatClient is already a TaggingChatClient registered by
        // AddChatClient, so we use it directly to avoid duplicate GenAI spans.
        var executorAgent = _chatClient.AsAIAgent(
            instructions: $"""
                You are a Python code-generation agent. Session ID: {sessionId}.
                When asked to create or fix Python code:
                1. On first attempt call write_file(content=<code>) to create the file.
                2. On revisions call edit_file(content=<revised_code>) to update it.
                Always pass complete, runnable Python code. Do not truncate.
                """,
            name: "executor",
            description: "Python code-generation agent",
            tools: [writeFileTool, editFileTool],
            loggerFactory: NullLoggerFactory.Instance,
            services: new ServiceCollection().BuildServiceProvider());

        Verdict? lastVerdict = null;

        // --- DelegateLoopEvaluator wraps IChecker ---
        var evaluator = new DelegateLoopEvaluator(async (ctx, ct) =>
        {
            finalIteration = ctx.Iteration;
            _logger.LogInformation("Evaluator invoked. SessionId={SessionId} Iteration={Iteration}", sessionId, ctx.Iteration);

            // Stall guard (P2)
            if (lastCodeHash == "__stalled__")
            {
                _logger.LogWarning("Evaluation: stall guard triggered. SessionId={SessionId}", sessionId);
                return LoopEvaluation.Continue("Stalled: the code was not changed. Try a different approach.");
            }

            if (lastPath is null)
            {
                _logger.LogWarning("Evaluation: no file written yet. SessionId={SessionId}", sessionId);
                return LoopEvaluation.Continue("No file was written. Call write_file with Python code first.");
            }

            lastVerdict = await _checker.ReviewAsync(sessionId, lastPath, ct).ConfigureAwait(false);
            _logger.LogInformation("Evaluation: verdict={VerdictOk} SessionId={SessionId} Path={Path}", lastVerdict.Ok, sessionId, lastPath);
            return lastVerdict.Ok
                ? LoopEvaluation.Stop()
                : LoopEvaluation.Continue(lastVerdict.Feedback!.ToString());
        });

        // --- LoopAgent cap ---
        var loopAgent = new LoopAgent(
            executorAgent,
            evaluator,
            new LoopAgentOptions { MaxIterations = maxIterations },
            loggerFactory: NullLoggerFactory.Instance);

        var session = await executorAgent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        var response = await loopAgent.RunAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            session,
            new AgentRunOptions(),
            cancellationToken).ConfigureAwait(false);

        var completed = lastPath is not null
            && finalIteration <= maxIterations
            && lastVerdict?.Ok == true;

        string finalCode = string.Empty;
        if (!string.IsNullOrEmpty(lastPath))
        {
            try
            {
                finalCode = await _tools.LoadFileAsync(lastPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load final code for UI. SessionId={SessionId} Path={Path}", sessionId, lastPath);
            }
        }

        _logger.LogInformation("Loop run completed. SessionId={SessionId} Iterations={Iterations} Completed={Completed}", sessionId, finalIteration, completed);

        return new LoopRunResult(
            SessionId: sessionId,
            Path: lastPath ?? string.Empty,
            Iterations: finalIteration,
            Completed: completed,
            FinalResponse: response.Text ?? string.Empty,
            FinalCode: finalCode);
    }

    private static string BuildFileName(string prompt)
    {
        var isFibo = prompt.Contains("fibonacci", StringComparison.OrdinalIgnoreCase)
                  || prompt.Contains("fib", StringComparison.OrdinalIgnoreCase);
        var stem = isFibo ? "fibo" : "main";
        return $"{stem}_{DateTimeOffset.UtcNow:yyyyMMddTHHmmss}.py";
    }

    private static string ComputeHash(string code)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(code);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

/// <summary>Result of a full loop run.</summary>
public sealed record LoopRunResult(
    string SessionId,
    string Path,
    int Iterations,
    bool Completed,
    string FinalResponse,
    string FinalCode);
