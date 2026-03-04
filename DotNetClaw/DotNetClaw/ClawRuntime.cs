using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;

namespace DotNetClaw;

/// <summary>
/// The agent runtime — maps session IDs to MAF AgentSessions and routes messages.
///
/// OpenClaw analogy:
///   Gateway receives message → lane queue → Agent Runtime → pi-agent-core → response
///   Twilio webhook / Slack event → ClawRuntime → AIAgent (MAF + Copilot SDK) → response
///
/// Session ID convention (per channel):
///   WhatsApp:  "whatsapp:+1234567890"
///   Slack DM:  "slack:dm:{user_id}"
///   Slack chan: "slack:{channel_id}:{user_id}"
/// Each session ID gets its own AgentSession, so history is per-user per-channel.
/// </summary>
public sealed class ClawRuntime(AIAgent agent, ILogger<ClawRuntime> logger)
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    // Per-session semaphore: prevents two concurrent requests on the same AgentSession
    // (AgentSession is not thread-safe; concurrent calls produce interleaved/garbled output)
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    /// <summary>Streaming variant — yields response text chunks as they arrive.</summary>
    public async IAsyncEnumerable<string> HandleStreamingAsync(
        string sessionId, string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(ct);
        try
        {
            logger.LogDebug("[{Session}] → {Preview}", sessionId, message[..Math.Min(80, message.Length)]);

            // The GitHub Copilot SDK bridge emits CUMULATIVE text on each update:
            // each update.Text contains the full response so far (not a delta).
            // We track how many chars we've already yielded and only emit the new suffix.
            var emitted = 0;
            await foreach (var update in agent.RunStreamingAsync(message, session, null, ct))
            {
                if (string.IsNullOrEmpty(update.Text) || update.Text.Length <= emitted) continue;
                yield return update.Text[emitted..];
                emitted = update.Text.Length;
            }
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Non-streaming handler. Collects the final complete response text.
    ///
    /// The Copilot SDK bridge emits a mix of small token deltas followed by a
    /// final update containing the full cumulative response. We cannot safely
    /// concatenate (doubles the text) or slice deltas cumulatively (corrupts
    /// multi-byte chars). Instead, we keep only the **last non-empty Text**
    /// which is the complete response in the bridge's cumulative protocol.
    /// </summary>
    public async Task<string> HandleAsync(string sessionId, string message, CancellationToken ct = default)
    {
        var session = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(ct);
        try
        {
            logger.LogDebug("[{Session}] → {Preview}", sessionId, message[..Math.Min(80, message.Length)]);

            string result = string.Empty;
            int updateCount = 0;
            await foreach (var update in agent.RunStreamingAsync(message, session, null, ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    result = update.Text;   // keep the last (most complete) update
                    updateCount++;
                }
            }
            logger.LogDebug("[{Session}] ← {Updates} updates, final length {Len}",
                sessionId, updateCount, result.Length);
            return result;
        }
        finally
        {
            sem.Release();
        }
    }

    private async ValueTask<AgentSession> GetOrCreateAsync(string sessionId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var existing)) return existing;
        var newSession = await agent.CreateSessionAsync(ct);
        return _sessions.GetOrAdd(sessionId, newSession);
    }
}
