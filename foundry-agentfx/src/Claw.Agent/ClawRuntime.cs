namespace Claw.Agent;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Claw.Agent.Agents;
using Claw.Core;
using Microsoft.Agents.AI;

public sealed class ClawRuntime(
    CoffeeshopWorkflow workflow,
    IToolSearchClient toolSearch,
    ILogger<ClawRuntime> logger)
    : IChatRuntime
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, List<ChatTurn>> _history = new();
    private readonly ConcurrentDictionary<string, string> _pendingRetries = new();
    private readonly ConcurrentDictionary<string, PendingOrder> _pendingOrders = new();
    private readonly ConcurrentDictionary<string, CustomerLookup> _pendingCustomers = new();

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
            var history = _history.GetOrAdd(sessionId, _ => []);
            var effectiveMessage = ResolveEffectiveMessage(sessionId, message);
            var forceRecoveryPrompt = !string.Equals(effectiveMessage, message, StringComparison.Ordinal);

            var fastPathResponse = await TryHandleCoffeeOrderFastPathAsync(sessionId, effectiveMessage, ct);
            if (fastPathResponse is not null)
            {
                activity?.SetTag("response.length", fastPathResponse.Length);
                activity?.SetTag("response.source", "coffee_fast_path");
                activity?.SetStatus(ActivityStatusCode.Ok);
                AppendHistory(history, effectiveMessage, fastPathResponse);
                _pendingRetries.TryRemove(sessionId, out _);
                return fastPathResponse;
            }

            if (forceRecoveryPrompt)
                session = await ResetSessionAsync(sessionId, ct);

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var modelMessage = forceRecoveryPrompt || attempt > 1
                    ? BuildRecoveryPrompt(history, effectiveMessage)
                    : effectiveMessage;

                try
                {
                    logger.LogDebug("[{Session}] → {Preview}", sessionId, modelMessage[..Math.Min(80, modelMessage.Length)]);

                    var response = await RunOnceAsync(modelMessage, session, ct);

                    if (string.IsNullOrWhiteSpace(response))
                    {
                        if (attempt < 3)
                        {
                            logger.LogWarning("[{Session}] Empty model response. Resetting session and retrying with recovered context.", sessionId);
                            session = await ResetSessionAsync(sessionId, ct);
                            continue;
                        }

                        _pendingRetries[sessionId] = effectiveMessage;
                        logger.LogWarning("[{Session}] Empty model response after retries; pending retry saved", sessionId);
                        return "Temporary model issue. I kept your last message; send \"try again\" to continue.";
                    }

                    activity?.SetTag("response.length", response.Length);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    ClawTelemetry.ResponseLengthChars.Record(response.Length,
                        new KeyValuePair<string, object?>("channel", channel),
                        new KeyValuePair<string, object?>("streaming", false));

                    AppendHistory(history, effectiveMessage, response);
                    _pendingRetries.TryRemove(sessionId, out _);
                    return response;
                }
                catch (Exception ex) when (attempt < 3 && IsPreviousResponseNotFound(ex))
                {
                    logger.LogWarning(ex, "[{Session}] Stale previous response id. Resetting session and retrying with recovered context.", sessionId);
                    session = await ResetSessionAsync(sessionId, ct);
                }
                catch (Exception ex) when (attempt < 3 && IsTooManyRequests(ex))
                {
                    logger.LogWarning(ex, "[{Session}] 429 Too Many Requests. Resetting session and retrying with recovered context.", sessionId);
                    await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
                    session = await ResetSessionAsync(sessionId, ct);
                }
            }

            _pendingRetries[sessionId] = message;
            return "Temporary model issue. I kept your last message; send \"try again\" to continue.";
        }
        catch (Exception ex)
        {
            _pendingRetries[sessionId] = message;
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

    private async Task<string> RunOnceAsync(string message, AgentSession session, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var update in workflow.RunStreamingAsync(message, session, ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
                sb.Append(update.Text);
        }
        return sb.ToString();
    }

    private async ValueTask<(AgentSession Session, bool IsNewSession)> GetOrCreateAsync(string sessionId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var existing)) return (existing, false);
        var newSession = await workflow.CreateSessionAsync(ct);
        var session = _sessions.GetOrAdd(sessionId, newSession);
        return (session, ReferenceEquals(session, newSession));
    }

    private async ValueTask<AgentSession> ResetSessionAsync(string sessionId, CancellationToken ct)
    {
        var newSession = await workflow.CreateSessionAsync(ct);
        _sessions.AddOrUpdate(sessionId, newSession, (_, _) => newSession);
        return newSession;
    }

    private static bool IsPreviousResponseNotFound(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("previous_response_not_found", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("previous_response_id", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTooManyRequests(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException hre && hre.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return true;
            if (current.Message.Contains("429", StringComparison.Ordinal)
                || current.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("TooManyRequests", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private string ResolveEffectiveMessage(string sessionId, string message)
    {
        if (IsRetryRequest(message) && _pendingRetries.TryGetValue(sessionId, out var pending))
            return pending;

        return message;
    }

    private async Task<string?> TryHandleCoffeeOrderFastPathAsync(string sessionId, string message, CancellationToken ct)
    {
        // If pending order exists with no customer, try to identify from message
        if (_pendingOrders.TryGetValue(sessionId, out var pending))
        {
            if (pending.CustomerId is null && LooksLikeCustomerIdentifier(message))
            {
                var customer = await LookupCustomerAsync(message, ct);
                if (customer is null)
                    return "I couldn't find that account. Please provide a different email, phone, or name.";

                _pendingOrders[sessionId] = pending with
                {
                    CustomerId = customer.Value.Id,
                    CustomerName = customer.Value.Name
                };
                _pendingCustomers[sessionId] = customer.Value;
                return null; // Let agent handle confirmation with full context
            }
        }

        // Detect simple coffee order pattern → store pending + ask for customer
        if (LooksLikeCoffeeOrder(message))
        {
            if (_pendingCustomers.TryGetValue(sessionId, out var customer))
            {
                _pendingOrders[sessionId] = new PendingOrder(message, customer.Id, customer.Name);
                return null; // Let agent handle order with customer context
            }

            _pendingOrders[sessionId] = new PendingOrder(message, null, null);
            return "I'll need your email, phone, or name first to process your order.";
        }

        // Store customer if email/phone provided first
        if (LooksLikeEmailOrPhone(message))
        {
            var customer = await LookupCustomerAsync(message, ct);
            if (customer is not null)
            {
                _pendingCustomers[sessionId] = customer.Value;
                return $"Hi {customer.Value.Name}! How can I help you today?";
            }
            return "I couldn't find that account. Please provide a different identifier or just tell me what you'd like to order.";
        }

        return null; // No deterministic match → pass to agent
    }

    private async Task<CustomerLookup?> LookupCustomerAsync(string query, CancellationToken ct)
    {
        var args = JsonSerializer.SerializeToElement(new { query });
        var result = await toolSearch.CallToolAsync("customer_lookup", args, ct);
        if (result is not JsonElement json || json.ValueKind != JsonValueKind.Object)
            return null;

        if (!json.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            return null;

        var name = json.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString())
            ? nameProp.GetString()!
            : id.GetString()!;

        return new CustomerLookup(id.GetString()!, name);
    }

    private static bool LooksLikeCoffeeOrder(string message)
    {
        return Regex.IsMatch(message, @"\b(\d+|one|two|three|four|five)\b", RegexOptions.IgnoreCase)
            && Regex.IsMatch(message, @"\b(latte|green\s+teas?|cappuccino|espresso|americano|mocha)\b", RegexOptions.IgnoreCase);
    }

    private static bool LooksLikeCustomerIdentifier(string message)
    {
        return message.Contains('@')
            || Regex.IsMatch(message, @"\d{3}[-\s]?\d{4}")
            || Regex.IsMatch(message.Trim(), @"^[A-Za-z]+(?:\s+[A-Za-z]+){0,2}$");
    }

    private static bool LooksLikeEmailOrPhone(string message)
    {
        return message.Contains('@')
            || Regex.IsMatch(message, @"\d{3}[-\s]?\d{4}");
    }

    private static bool IsRetryRequest(string message)
    {
        var normalized = message.Trim();
        return normalized.Equals("try again", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("retry", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("please retry", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendHistory(List<ChatTurn> history, string userMessage, string assistantMessage)
    {
        history.Add(new ChatTurn("User", userMessage));
        history.Add(new ChatTurn("Assistant", assistantMessage));

        const int maxTurns = 12;
        if (history.Count > maxTurns)
            history.RemoveRange(0, history.Count - maxTurns);
    }

    private static string BuildRecoveryPrompt(IReadOnlyList<ChatTurn> history, string currentMessage)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Previous model request failed before completing. Continue the same conversation using the recovered context below.");
        sb.AppendLine("Do not ask the user to repeat details already present. Treat CURRENT USER MESSAGE as the latest user message.");
        sb.AppendLine();
        sb.AppendLine("RECENT CONVERSATION:");

        foreach (var turn in history)
            sb.AppendLine($"{turn.Role}: {turn.Text}");

        sb.AppendLine();
        sb.AppendLine("CURRENT USER MESSAGE:");
        sb.AppendLine(currentMessage);
        return sb.ToString();
    }

    private static string GetChannelFromSessionId(string sessionId)
    {
        var index = sessionId.IndexOf(':');
        return index > 0 ? sessionId[..index] : "unknown";
    }

    private sealed record ChatTurn(string Role, string Text);
    private readonly record struct CustomerLookup(string Id, string Name);
    private sealed record PendingOrder(string OrderText, string? CustomerId, string? CustomerName);
}
