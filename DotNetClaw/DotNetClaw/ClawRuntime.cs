using System.Collections.Concurrent;
using System.Diagnostics;
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
        var (session, isNewSession) = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var channel = GetChannelFromSessionId(sessionId);

        ClawTelemetry.RequestsTotal.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("streaming", true));

        var start = Stopwatch.GetTimestamp();

        using var activity = ClawTelemetry.ActivitySource.StartActivity("clawruntime.handle.streaming", ActivityKind.Internal);
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("session.is_new", isNewSession);
        activity?.SetTag("channel", channel);
        activity?.SetTag("message.length", message.Length);

        await sem.WaitAsync(ct);
        var succeeded = false;
        try
        {
            logger.LogDebug("[{Session}] → {Preview}", sessionId, message[..Math.Min(80, message.Length)]);

            var chunkCount = 0;
            var totalResponseChars = 0;

            await foreach (var update in agent.RunStreamingAsync(message, session, null, ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    chunkCount++;
                    totalResponseChars += update.Text.Length;
                    yield return update.Text;
                }
            }

            activity?.SetTag("response.chunks", chunkCount);
            activity?.SetTag("response.length", totalResponseChars);
            activity?.SetStatus(ActivityStatusCode.Ok);
            ClawTelemetry.ResponseLengthChars.Record(totalResponseChars,
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("streaming", true));
            succeeded = true;
        }
        finally
        {
            if (!succeeded)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
                ClawTelemetry.ErrorsTotal.Add(1,
                    new KeyValuePair<string, object?>("channel", channel),
                    new KeyValuePair<string, object?>("streaming", true));
            }
            sem.Release();
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ClawTelemetry.RequestDurationMs.Record(elapsedMs,
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("streaming", true));
        }
    }

    /// <summary>
    /// Non-streaming handler. Collects the final complete response text.
    /// </summary>
    public async Task<string> HandleAsync(string sessionId, string message, CancellationToken ct = default)
    {
        var (session, isNewSession) = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var channel = GetChannelFromSessionId(sessionId);

        ClawTelemetry.RequestsTotal.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("streaming", false));

        var start = Stopwatch.GetTimestamp();

        using var activity = ClawTelemetry.ActivitySource.StartActivity("clawruntime.handle", ActivityKind.Internal);
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("session.is_new", isNewSession);
        activity?.SetTag("channel", channel);
        activity?.SetTag("message.length", message.Length);

        await sem.WaitAsync(ct);
        try
        {
            logger.LogDebug("[{Session}] → {Preview}", sessionId, message[..Math.Min(80, message.Length)]);

            var sb = new System.Text.StringBuilder();
            await foreach (var update in agent.RunStreamingAsync(message, session, null, ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    sb.Append(update.Text);
                }
            }
            var response = sb.ToString();

            activity?.SetTag("response.length", response.Length);
            activity?.SetStatus(ActivityStatusCode.Ok);

            ClawTelemetry.ResponseLengthChars.Record(response.Length,
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("streaming", false));

            return response;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("error.type", ex.GetType().Name);
            activity?.SetTag("error.message", ex.Message);
            ClawTelemetry.ErrorsTotal.Add(1,
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("streaming", false));
            throw;
        }
        finally
        {
            sem.Release();
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            ClawTelemetry.RequestDurationMs.Record(elapsedMs,
                new KeyValuePair<string, object?>("channel", channel),
                new KeyValuePair<string, object?>("streaming", false));
        }
    }

    private async ValueTask<(AgentSession Session, bool IsNewSession)> GetOrCreateAsync(string sessionId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var existing)) return (existing, false);
        var newSession = await agent.CreateSessionAsync(ct);
        var session = _sessions.GetOrAdd(sessionId, newSession);
        return (session, ReferenceEquals(session, newSession));
    }

    private static string GetChannelFromSessionId(string sessionId)
    {
        var index = sessionId.IndexOf(':');
        return index > 0 ? sessionId[..index] : "unknown";
    }
}
