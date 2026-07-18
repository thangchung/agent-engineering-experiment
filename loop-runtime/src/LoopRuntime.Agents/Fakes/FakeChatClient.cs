using Microsoft.Extensions.AI;

namespace LoopRuntime.Agents.Fakes;

/// <summary>
/// Deterministic fake IChatClient for Microsoft.Extensions.AI 10.6.0.
/// Iteration 1: calls write_file with buggy code (NameError — calls wrong function name).
/// Iteration 2: calls edit_file with buggy code (IndentationError — return inside loop).
/// Iteration 3+: calls edit_file with correct fibonacci code that runs clean.
/// After any tool result: returns "Done. File: {path}" so the evaluator sees a clean response.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    // Code variants scripted to match the mock.html demo states
    private const string BuggyCode1 =
        "def fibonacci(n):\n    a, b = 0, 1\n    result = []\n    for _ in range(n):\n        result.append(a)\n        a, b = b, a + b\n    return result\n\nfib(20)  # wrong name\n";

    private const string BuggyCode2 =
        "def fibonacci(n):\n    a, b = 0, 1\n    result = []\n    for _ in range(n):\n        result.append(a)\n        a, b = b, a + b\n        return result  # bad indent\n\nfor x in fibonacci(20):\n    print(x)\n";

    private const string CorrectCode =
        "def fibonacci(n: int) -> list[int]:\n    a, b = 0, 1\n    result = []\n    for _ in range(n):\n        result.append(a)\n        a, b = b, a + b\n    return result\n\nif __name__ == '__main__':\n    for x in fibonacci(20):\n        print(x)\n";

    // Count of non-tool-result calls (fresh LLM turns per LoopAgent iteration)
    private int _agentCallCount;

    public ChatClientMetadata Metadata { get; } = new("fake", null, "fake-model");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var messageList = messages.ToList();

        // Post-tool-result call: acknowledge with file path in text
        if (messageList.Any(static m => m.Role == ChatRole.Tool))
        {
            var path = messageList
                .Last(static m => m.Role == ChatRole.Tool)
                .Contents.OfType<FunctionResultContent>()
                .FirstOrDefault()?.Result?.ToString() ?? string.Empty;

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, $"Done. File: {path}"))
            {
                ModelId = "fake-model",
                Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 },
            });
        }

        var lastUserText = messageList
            .LastOrDefault(static m => m.Role == ChatRole.User)
            ?.Text ?? string.Empty;

        // Checker review prompt: return first stderr line as the reason.
        if (lastUserText.Contains("Path:", StringComparison.Ordinal)
            && lastUserText.Contains("Stderr:", StringComparison.Ordinal))
        {
            var reason = lastUserText
                .Split('\n')
                .Select(static x => x.Trim())
                .FirstOrDefault(static x => x.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    || x.Contains("Traceback", StringComparison.OrdinalIgnoreCase))
                ?? "Execution failed.";

            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, reason))
            {
                ModelId = "fake-model",
                Usage = new UsageDetails { InputTokenCount = 20, OutputTokenCount = 5 },
            });
        }

        // Fresh call for this LoopAgent iteration
        _agentCallCount++;

        var code = _agentCallCount switch
        {
            1 => BuggyCode1,
            2 => BuggyCode2,
            _ => CorrectCode,
        };

        // First call → write_file; subsequent → edit_file (overwrite with fix)
        var toolName = _agentCallCount == 1 ? "write_file" : "edit_file";
        var callId = Guid.NewGuid().ToString("N");
        var toolCall = new FunctionCallContent(callId, toolName,
            new Dictionary<string, object?> { ["content"] = code });

        return Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, [toolCall]))
        {
            ModelId = "fake-model",
            FinishReason = ChatFinishReason.ToolCalls,
            Usage = new UsageDetails { InputTokenCount = 50, OutputTokenCount = 40 },
        });
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("FakeChatClient does not support streaming.");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
