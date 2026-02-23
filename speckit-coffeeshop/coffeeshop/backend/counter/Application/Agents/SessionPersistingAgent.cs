using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CoffeeShop.Counter.Application.Agents;

/// <summary>
/// Wraps an <see cref="AIAgent"/> and keeps one <see cref="AgentSession"/> alive per
/// AG-UI thread, so that the underlying provider (GitHub Copilot) can <em>resume</em>
/// the same conversation instead of creating a new CLI session for every message.
/// </summary>
/// <remarks>
/// <para>
/// <c>MapAGUI</c> (from <c>Microsoft.Agents.AI.Hosting.AGUI.AspNetCore</c>) always
/// passes <see langword="null"/> as the <see cref="AgentSession"/> parameter when
/// it invokes <c>RunStreamingAsync</c>.  When the inner agent (<c>GitHubCopilotAgent</c>)
/// receives a null session it calls <c>CopilotClient.CreateSessionAsync()</c>, which
/// registers a brand-new GitHub Copilot CLI session — one per user message.
/// </para>
/// <para>
/// This decorator intercepts <c>RunCoreStreamingAsync</c>, looks up (or lazily creates)
/// an <see cref="AgentSession"/> keyed by the AG-UI <c>threadId</c> that arrives in
/// <c>options.AdditionalProperties["ag_ui_thread_id"]</c>, and forwards that session
/// to the inner agent.  After the first turn the inner agent has already set
/// <c>GitHubCopilotAgentSession.SessionId</c> on the cached object; subsequent turns
/// therefore call <c>CopilotClient.ResumeSessionAsync</c>, reusing the same CLI session.
/// </para>
/// </remarks>
public sealed class SessionPersistingAgent : DelegatingAIAgent
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();

    /// <summary>
    /// Initialises a new <see cref="SessionPersistingAgent"/> wrapping <paramref name="innerAgent"/>.
    /// </summary>
    public SessionPersistingAgent(AIAgent innerAgent) : base(innerAgent) { }

    // -----------------------------------------------------------------------
    // Core override — session injection
    // -----------------------------------------------------------------------

    /// <inheritdoc/>
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // If MapAGUI (or any caller) already supplies a session, respect it.
        AgentSession sessionToUse = session
            ?? await ResolveSessionAsync(options, cancellationToken).ConfigureAwait(false);

        await foreach (var update in InnerAgent
            .RunStreamingAsync(messages, sessionToUse, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return update;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns an existing <see cref="AgentSession"/> for the current AG-UI thread,
    /// or creates a fresh one (from the inner agent) and caches it.
    /// </summary>
    private async ValueTask<AgentSession> ResolveSessionAsync(
        AgentRunOptions? options,
        CancellationToken cancellationToken)
    {
        string? threadId = TryGetThreadId(options);

        if (threadId is null)
        {
            // No thread context — fall back to a transient session (old behaviour).
            return await InnerAgent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_sessions.TryGetValue(threadId, out AgentSession? existing))
        {
            return existing;
        }

        // First message on this thread: create a GitHubCopilotAgentSession with
        // SessionId == null. The inner agent will call CreateSessionAsync on the
        // Copilot SDK, set SessionId on this object, and we reuse it from here on.
        AgentSession newSession = await InnerAgent
            .CreateSessionAsync(cancellationToken)
            .ConfigureAwait(false);

        // Allow the first writer to win; discard any race duplicate.
        _sessions.TryAdd(threadId, newSession);
        return _sessions[threadId];
    }

    /// <summary>
    /// Extracts <c>ag_ui_thread_id</c> from <c>options.AdditionalProperties</c>,
    /// or returns <see langword="null"/> if it is absent.
    /// </summary>
    private static string? TryGetThreadId(AgentRunOptions? options) =>
        options is ChatClientAgentRunOptions
        {
            ChatOptions.AdditionalProperties: { } props
        }
        && props.TryGetValue("ag_ui_thread_id", out object? raw)
        && raw is string id
            ? id
            : null;
}
