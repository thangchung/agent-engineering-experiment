namespace Claw.Api;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Claw.Api.Agents;
using Microsoft.Agents.AI;

public sealed class ClawRuntime(CoffeeshopWorkflow workflow, ILogger<ClawRuntime> logger)
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async IAsyncEnumerable<string> HandleStreamingAsync(
        string sessionId,
        string message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (session, isNewSession) = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var channel = GetChannelFromSessionId(sessionId);

        ClawTelemetry.RequestsTotal.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("streaming", true));

        var start = Stopwatch.GetTimestamp();

        using var activity = ClawTelemetry.ActivitySource.StartActivity("claw.handle.streaming", ActivityKind.Server);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.system", "microsoft_agent_framework");
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

            await foreach (var update in workflow.RunStreamingAsync(message, session, ct))
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

    public async Task<string> HandleAsync(string sessionId, string message, CancellationToken ct = default)
    {
        var (session, isNewSession) = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        var channel = GetChannelFromSessionId(sessionId);

        ClawTelemetry.RequestsTotal.Add(1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("streaming", false));

        var start = Stopwatch.GetTimestamp();

        using var activity = ClawTelemetry.ActivitySource.StartActivity("claw.handle", ActivityKind.Server);
        activity?.SetTag("gen_ai.operation.name", "chat");
        activity?.SetTag("gen_ai.system", "microsoft_agent_framework");
        activity?.SetTag("session.id", sessionId);
        activity?.SetTag("session.is_new", isNewSession);
        activity?.SetTag("channel", channel);
        activity?.SetTag("message.length", message.Length);

        await sem.WaitAsync(ct);
        try
        {
            logger.LogDebug("[{Session}] → {Preview}", sessionId, message[..Math.Min(80, message.Length)]);

            var sb = new System.Text.StringBuilder();
            await foreach (var update in workflow.RunStreamingAsync(message, session, ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                    sb.Append(update.Text);
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
        var newSession = await workflow.CreateSessionAsync(ct);
        var session = _sessions.GetOrAdd(sessionId, newSession);
        return (session, ReferenceEquals(session, newSession));
    }

    private static string GetChannelFromSessionId(string sessionId)
    {
        var index = sessionId.IndexOf(':');
        return index > 0 ? sessionId[..index] : "unknown";
    }
}

