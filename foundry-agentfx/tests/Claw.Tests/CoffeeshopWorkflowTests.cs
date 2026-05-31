using System.Runtime.CompilerServices;
using System.Text.Json;
using Claw.Api.Agents;
using Coffeeshop.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claw.Tests;

public class CoffeeshopWorkflowTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"workflow-test-{Guid.NewGuid():N}");

    private IConfiguration CreateAuditConfig()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Audit:OrdersPath"] = _tempDir })
            .Build();
    }

    [Fact]
    public async Task Workflow_Passes_Through_Text_Updates()
    {
        var updates = new[]
        {
            MakeTextUpdate("Hello"),
            MakeTextUpdate(" world"),
        };
        var ordering = new FakeOrderingAgent(updates);
        var workflow = new CoffeeshopWorkflow(ordering, CreateAuditConfig(), NullLoggerFactory.Instance);
        var session = await workflow.CreateSessionAsync();

        var result = new List<string>();
        await foreach (var update in workflow.RunStreamingAsync("hi", session))
        {
            if (!string.IsNullOrEmpty(update.Text))
                result.Add(update.Text);
        }

        Assert.Equal(["Hello", " world"], result);
    }

    [Fact]
    public async Task Workflow_Retries_Once_On_Transient_PreUpdate_Cancellation()
    {
        var updates = new[]
        {
            MakeTextUpdate("Recovered"),
            MakeTextUpdate(" response"),
        };

        var ordering = new FlakyOrderingAgent(updates);
        var workflow = new CoffeeshopWorkflow(ordering, CreateAuditConfig(), NullLoggerFactory.Instance);
        var session = await workflow.CreateSessionAsync();

        var result = new List<string>();
        await foreach (var update in workflow.RunStreamingAsync("hi", session))
        {
            if (!string.IsNullOrEmpty(update.Text))
                result.Add(update.Text);
        }

        Assert.Equal(["Recovered", " response"], result);
    }

    [Fact]
    public async Task Workflow_Triggers_Audit_On_OrderSubmit_Result()
    {
        var order = new OrderResult(
            "ORD-WFLOW-001", "c1", "Charlie",
            [new OrderItem("latte", "Latte", 1, 4.50m)],
            4.50m,
            new DateTimeOffset(2026, 5, 25, 12, 0, 0, TimeSpan.Zero));

        var callId = "call-abc-123";
        var orderJson = JsonSerializer.Serialize(order, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var updates = new[]
        {
            MakeCallUpdate(callId, "call_tool", new Dictionary<string, object?> { ["name"] = "order_submit", ["argumentsJson"] = "{}" }),
            MakeResultUpdate(callId, orderJson),
            MakeTextUpdate("Order placed!"),
        };
        var ordering = new FakeOrderingAgent(updates);
        var workflow = new CoffeeshopWorkflow(ordering, CreateAuditConfig(), NullLoggerFactory.Instance);
        var session = await workflow.CreateSessionAsync();

        await foreach (var _ in workflow.RunStreamingAsync("submit order", session)) { }

        var auditPath = Path.Combine(_tempDir, "2026-05-25", "ORD-WFLOW-001.md");
        Assert.True(File.Exists(auditPath), $"Audit file not created at {auditPath}");
        var content = await File.ReadAllTextAsync(auditPath);
        Assert.Contains("ORD-WFLOW-001", content);
        Assert.Contains("Charlie", content);
    }

    [Fact]
    public async Task Workflow_Does_Not_Audit_Non_OrderSubmit_Calls()
    {
        var callId = "call-xyz-456";
        var updates = new[]
        {
            MakeCallUpdate(callId, "call_tool", new Dictionary<string, object?> { ["name"] = "menu_list_items", ["argumentsJson"] = "{}" }),
            MakeResultUpdate(callId, """[{"id":"latte"}]"""),
            MakeTextUpdate("Here are the menu items."),
        };
        var ordering = new FakeOrderingAgent(updates);
        var workflow = new CoffeeshopWorkflow(ordering, CreateAuditConfig(), NullLoggerFactory.Instance);
        var session = await workflow.CreateSessionAsync();

        await foreach (var _ in workflow.RunStreamingAsync("list menu", session)) { }

        Assert.False(Directory.Exists(_tempDir), "No audit file should exist for non-order tool calls");
    }

    private static AgentResponseUpdate MakeTextUpdate(string text) =>
        new(null, text);

    private static AgentResponseUpdate MakeCallUpdate(string callId, string name, Dictionary<string, object?> args)
    {
        var call = new FunctionCallContent(callId, name, args);
        return new AgentResponseUpdate(null, (IList<AIContent>)[call]);
    }

    private static AgentResponseUpdate MakeResultUpdate(string callId, string result)
    {
        var res = new FunctionResultContent(callId, (object)result);
        return new AgentResponseUpdate(null, (IList<AIContent>)[res]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

internal sealed class FakeOrderingAgent(AgentResponseUpdate[] updates) : IOrderingAgent
{
    public string Name => "FakeOrderingAgent";

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult<AgentSession>(new FakeSession());

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var u in updates)
        {
            await Task.Yield();
            yield return u;
        }
    }
}

internal sealed class FlakyOrderingAgent(AgentResponseUpdate[] updates) : IOrderingAgent
{
    private int _runCount;

    public string Name => "FlakyOrderingAgent";

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
        => ValueTask.FromResult<AgentSession>(new FakeSession());

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _runCount++;
        if (_runCount == 1)
            throw new TaskCanceledException("Transient stream failure");

        foreach (var u in updates)
        {
            await Task.Yield();
            yield return u;
        }
    }
}

internal sealed class FakeSession : AgentSession
{
}
