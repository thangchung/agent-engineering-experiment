using System.Runtime.CompilerServices;
using System.Text.Json;
using LoopRuntime.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoopRuntime.Checker;

/// <summary>
/// A2A-facing agent that wraps <see cref="IChecker"/>.
/// Expects a user message of the form: "REVIEW {sessionId} {path}".
/// Returns the <see cref="Verdict"/> as JSON text.
/// </summary>
public sealed class CheckerAIAgent : AIAgent
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CheckerAIAgent> _logger;

    public CheckerAIAgent(IServiceScopeFactory scopeFactory, ILogger<CheckerAIAgent> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public override string Name => "checker";
    public override string Description => "Reviews Python code and returns a verdict.";

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
        => new(new EmptyAgentSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(JsonDocument.Parse("{}").RootElement.Clone());

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        => new(new EmptyAgentSession());

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = messages
            .Where(m => m.Role == ChatRole.User)
            .Select(m => m.Text)
            .LastOrDefault(static t => !string.IsNullOrWhiteSpace(t));

        _logger.LogInformation("A2A review request received. Text={Text}", text);

        if (!TryParseRequest(text, out var sessionId, out var path) || string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(path))
        {
            return Response("Invalid request. Expected: REVIEW <sessionId> <path>");
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var checker = scope.ServiceProvider.GetRequiredService<IChecker>();
            var verdict = await checker.ReviewAsync(sessionId!, path!, cancellationToken).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(verdict, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            _logger.LogInformation("A2A review completed. SessionId={SessionId} Verdict={VerdictOk}", sessionId, verdict.Ok);
            return Response(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A2A review failed. SessionId={SessionId} Path={Path}", sessionId, path);
            return Response($"{{\"ok\":false,\"feedback\":{{\"reason\":\"{ex.Message}\",\"fixes\":[]}},\"run\":null}}");
        }
    }

    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunCoreStreamingAsyncImpl(messages, session, options, cancellationToken);
    }

    private async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsyncImpl(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        var text = response.Text ?? string.Empty;
        yield return new AgentResponseUpdate(ChatRole.Assistant, text);
    }

    private static bool TryParseRequest(string? text, out string? sessionId, out string? path)
    {
        sessionId = null;
        path = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !string.Equals(parts[0], "REVIEW", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sessionId = parts[1];
        path = string.Join(' ', parts[2..]);
        return true;
    }

    private static AgentResponse Response(string text)
        => new([new ChatMessage(ChatRole.Assistant, text)]);

    private sealed class EmptyAgentSession : AgentSession
    {
    }
}
