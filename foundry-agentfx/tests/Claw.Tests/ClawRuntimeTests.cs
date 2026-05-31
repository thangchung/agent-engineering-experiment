using System.Runtime.CompilerServices;
using Claw.Api;
using Claw.Api.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claw.Tests;

public class ClawRuntimeTests
{
    [Fact]
    public async Task HandleAsync_ResetsSession_AndRetries_OnPreviousResponseNotFound()
    {
        var ordering = new PreviousResponseNotFoundOnceOrderingAgent();
        var config = new ConfigurationBuilder().Build();
        var workflow = new CoffeeshopWorkflow(ordering, config, NullLoggerFactory.Instance);
        var runtime = new ClawRuntime(workflow, NullLogger<ClawRuntime>.Instance);

        var result = await runtime.HandleAsync("slack:test-channel:test-user", "1 green tea");

        Assert.Equal("Recovered response", result);
        Assert.Equal(2, ordering.AttemptCount);
        Assert.Equal(2, ordering.SessionCreateCount);
    }

    [Fact]
    public async Task HandleAsync_ReturnsFallback_WhenModelReturnsEmpty()
    {
        var ordering = new EmptyResponseOrderingAgent();
        var config = new ConfigurationBuilder().Build();
        var workflow = new CoffeeshopWorkflow(ordering, config, NullLoggerFactory.Instance);
        var runtime = new ClawRuntime(workflow, NullLogger<ClawRuntime>.Instance);

        var result = await runtime.HandleAsync("slack:test-channel:test-user", "1 green tea");

        Assert.Equal("Sorry, I did not get a response. Please try again.", result);
        Assert.Equal(1, ordering.AttemptCount);
    }

    [Fact]
    public async Task HandleAsync_PassesMessageThrough_WhenModelReplies()
    {
        var ordering = new PendingConfirmOrderingAgent();
        var config = new ConfigurationBuilder().Build();
        var workflow = new CoffeeshopWorkflow(ordering, config, NullLoggerFactory.Instance);
        var runtime = new ClawRuntime(workflow, NullLogger<ClawRuntime>.Instance);

        var first = await runtime.HandleAsync("slack:test-channel:test-user", "1 green tea");
        var second = await runtime.HandleAsync("slack:test-channel:test-user", "confirm");

        Assert.Equal("Sorry, I did not get a response. Please try again.", first);
        Assert.Equal("Order placed", second);
        Assert.Equal(2, ordering.AttemptCount);
        Assert.Equal("confirm", ordering.MessagesSeen[1]); // no rewriting
    }
}

internal sealed class PreviousResponseNotFoundOnceOrderingAgent : IOrderingAgent
{
    public int AttemptCount { get; private set; }
    public int SessionCreateCount { get; private set; }

    public string Name => "PreviousResponseNotFoundOnceOrderingAgent";

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
    {
        SessionCreateCount++;
        return ValueTask.FromResult<AgentSession>(new LocalFakeSession());
    }

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        AttemptCount++;
        await Task.Yield();

        if (AttemptCount == 1)
            throw new InvalidOperationException("HTTP 400 (invalid_request_error: previous_response_not_found) Parameter: previous_response_id");

        yield return new AgentResponseUpdate(null, "Recovered response");
    }

    private sealed class LocalFakeSession : AgentSession
    {
    }
}

internal sealed class EmptyResponseOrderingAgent : IOrderingAgent
{
    public int AttemptCount { get; private set; }

    public string Name => "EmptyResponseOrderingAgent";

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult<AgentSession>(new LocalFakeSession());

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        AttemptCount++;
        await Task.Yield();
        yield break;
    }

    private sealed class LocalFakeSession : AgentSession
    {
    }
}

internal sealed class PendingConfirmOrderingAgent : IOrderingAgent
{
    public int AttemptCount { get; private set; }
    public List<string> MessagesSeen { get; } = [];

    public string Name => "PendingConfirmOrderingAgent";

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult<AgentSession>(new LocalFakeSession());

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        AttemptCount++;
        MessagesSeen.Add(message);
        await Task.Yield();

        if (AttemptCount == 1)
            yield break;

        yield return new AgentResponseUpdate(null, "Order placed");
    }

    private sealed class LocalFakeSession : AgentSession
    {
    }
}
