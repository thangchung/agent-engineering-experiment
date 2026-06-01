namespace Claw.Api.Agents;

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Coffeeshop.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

public interface IOrderingAgent
{
    string Name { get; }
    ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default);
    IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(string message, AgentSession session, CancellationToken ct = default);
}

/// <summary>
/// Orchestrates the coffeeshop ordering flow using MAF WorkflowBuilder.
/// Topology: OrderingExecutor → (concurrent) AuditExecutor
/// OrderingExecutor streams agent updates and sends any OrderResult messages
/// to AuditExecutor, which runs concurrently as a side-effect.
/// </summary>
public sealed class CoffeeshopWorkflow(
    IOrderingAgent ordering,
    IConfiguration config,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<CoffeeshopWorkflow>();

    internal static bool IsPreviousResponseNotFound(Exception ex)
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

    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default)
        => ordering.CreateSessionAsync(ct);

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message,
        AgentSession session,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var activity = ClawTelemetry.ActivitySource.StartActivity(
            "coffeeshop.workflow.run", ActivityKind.Internal);
        activity?.SetTag("workflow.executor.entry", "ordering-executor");
        activity?.SetTag("message.length", message.Length);

        _logger.LogDebug("[Workflow] Starting run — message length={Len}", message.Length);

        var orderingExec = new OrderingWorkflowExecutor(ordering, session, _logger);
        var auditExec = new AuditWorkflowExecutor(config, loggerFactory.CreateLogger<AuditWorkflowExecutor>());

        var workflow = new WorkflowBuilder(orderingExec)
            .AddEdge(orderingExec, auditExec)
            .Build();

        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, message);

        var chunkCount = 0;
        Exception? executionError = null;
        await foreach (var evt in run.WatchStreamAsync().WithCancellation(ct))
        {
            switch (evt)
            {
                case AgentResponseUpdateEvent updateEvt:
                    chunkCount++;
                    yield return updateEvt.Update;
                    break;
                case WorkflowErrorEvent errEvt:
                    activity?.SetStatus(ActivityStatusCode.Error, errEvt.Exception?.Message);
                    if (errEvt.Exception is not null && IsPreviousResponseNotFound(errEvt.Exception))
                    {
                        _logger.LogWarning("[Workflow] Retriable stale response id detected");
                        executionError ??= errEvt.Exception;
                    }
                    else
                    {
                        _logger.LogError(errEvt.Exception, "[Workflow] Error");
                        executionError ??= errEvt.Exception ?? new InvalidOperationException("Workflow execution failed.");
                    }
                    break;
                case ExecutorFailedEvent failEvt:
                    activity?.SetStatus(ActivityStatusCode.Error, failEvt.Data?.ToString());
                    if (failEvt.Data is Exception failEx && IsPreviousResponseNotFound(failEx))
                    {
                        _logger.LogWarning("[Workflow] Executor '{Id}' hit retriable stale response id", failEvt.ExecutorId);
                        executionError ??= failEx;
                    }
                    else
                    {
                        _logger.LogError("[Workflow] Executor '{Id}' failed: {Data}", failEvt.ExecutorId, failEvt.Data);
                        executionError ??= failEvt.Data as Exception
                            ?? new InvalidOperationException(failEvt.Data?.ToString() ?? "Workflow executor failed.");
                    }
                    break;
            }
        }

        if (executionError is not null)
            throw executionError;

        activity?.SetTag("response.chunks", chunkCount);
        activity?.SetStatus(ActivityStatusCode.Ok);
        _logger.LogDebug("[Workflow] Completed — chunks={Chunks}", chunkCount);
    }
}

/// <summary>
/// Entry executor: runs the ordering agent (wrapping an AIAgent), fires AgentResponseUpdateEvents
/// for streaming output, and routes completed OrderResult messages to connected executors.
/// </summary>
[SendsMessage(typeof(OrderResult))]
internal sealed class OrderingWorkflowExecutor(
    IOrderingAgent ordering,
    AgentSession session,
    ILogger logger)
    : Executor<string>("ordering-executor")
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public override async ValueTask HandleAsync(
        string message, IWorkflowContext context, CancellationToken ct = default)
    {
        using var activity = ClawTelemetry.ActivitySource.StartActivity(
            "coffeeshop.ordering.execute", ActivityKind.Internal);
        activity?.SetTag("executor.id", this.Id);
        activity?.SetTag("agent.name", ordering.Name);

        logger.LogInformation("[Ordering] Starting — agent={Agent}", ordering.Name);

        var updateCount = 0;
        var ordersSent = 0;
        var completed = false;

        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                var pendingOrderCalls = new HashSet<string>();
                var attemptUpdateCount = 0;

                try
                {
                    await foreach (var update in ordering.RunStreamingAsync(message, session, ct))
                    {
                        attemptUpdateCount++;
                        updateCount++;
                        await context.AddEventAsync(new AgentResponseUpdateEvent(this.Id, update), ct);

                        if (update.Contents is null) continue;

                        foreach (var content in update.Contents)
                        {
                            if (content is FunctionCallContent call
                                && call.Name == "call_tool"
                                && call.Arguments?.TryGetValue("name", out var toolName) == true
                                && toolName?.ToString() == "order_submit"
                                && call.CallId is not null)
                            {
                                pendingOrderCalls.Add(call.CallId);
                                logger.LogDebug("[Ordering] Detected order_submit call {CallId}", call.CallId);
                                activity?.AddEvent(new ActivityEvent("order_submit.detected",
                                    tags: new ActivityTagsCollection { ["call.id"] = call.CallId }));
                            }
                            else if (content is FunctionResultContent result
                                && result.CallId is not null
                                && pendingOrderCalls.Remove(result.CallId))
                            {
                                var order = await TrySendOrderAsync(result, context, ct);
                                if (order is not null)
                                {
                                    await context.AddEventAsync(new AgentResponseUpdateEvent(
                                        this.Id,
                                        new AgentResponseUpdate(ChatRole.Assistant, FormatOrderConfirmation(order))), ct);
                                    ordersSent++;
                                }
                            }
                        }
                    }

                    completed = true;
                    break;
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt == 1 && attemptUpdateCount == 0)
                {
                    logger.LogWarning(ex, "[Ordering] Transient cancellation before first update. Retrying once.");
                }
            }

            if (!completed)
                throw new InvalidOperationException("Ordering stream did not complete.");

            activity?.SetTag("updates.count", updateCount);
            activity?.SetTag("orders.routed", ordersSent);
            activity?.SetStatus(ActivityStatusCode.Ok);
            logger.LogInformation("[Ordering] Completed — updates={Updates} orders={Orders}", updateCount, ordersSent);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            if (CoffeeshopWorkflow.IsPreviousResponseNotFound(ex))
                logger.LogWarning("[Ordering] Retriable stale response id detected");
            else
                logger.LogError(ex, "[Ordering] Failed");
            throw;
        }
    }

    private async ValueTask<OrderResult?> TrySendOrderAsync(
        FunctionResultContent result, IWorkflowContext context, CancellationToken ct)
    {
        try
        {
            var json = result.Result?.ToString();
            if (string.IsNullOrWhiteSpace(json)) return null;

            // Skip if text doesn't look like JSON
            var trimmed = json.AsSpan().TrimStart();
            if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
            {
                logger.LogDebug("[Ordering] Tool result not JSON; skipping OrderResult parse: {Preview}",
                    json.Length > 50 ? json[..50] : json);
                return null;
            }

            var order = JsonSerializer.Deserialize<OrderResult>(json, _jsonOpts);
            if (order is null || string.IsNullOrEmpty(order.OrderId)) return null;

            logger.LogDebug("[Ordering] Routing OrderResult {OrderId} → audit", order.OrderId);
            await context.SendMessageAsync(order, cancellationToken: ct);
            return order;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Workflow] Failed to parse OrderResult: {Message}", ex.Message);
            return null;
        }
    }

    private static string FormatOrderConfirmation(OrderResult order)
    {
        var total = order.Total.ToString("F2", CultureInfo.InvariantCulture);
        var items = string.Join(", ", order.Items.Select(i => $"{i.Quantity} x {i.MenuItemName}"));
        return $"Order placed: {items}. Total ${total}. Order ID: {order.OrderId}.";
    }
}

/// <summary>
/// Receives OrderResult messages and logs them to the file-based audit trail.
/// Runs concurrently alongside the ordering agent response streaming.
/// Absorbs all logic previously in AuditAgent.
/// </summary>
public sealed class AuditWorkflowExecutor : Executor<OrderResult>
{
    private readonly string _ordersRoot;
    private readonly ILogger _logger;

    public string Name => "AuditAgent";

    public AuditWorkflowExecutor(IConfiguration config, ILogger logger)
        : base("AuditAgent")
    {
        _ordersRoot = config["Audit:OrdersPath"] ?? "./orders";
        _logger = logger;
    }

    public override async ValueTask HandleAsync(
        OrderResult order, IWorkflowContext context, CancellationToken ct = default)
    {
        using var activity = ClawTelemetry.ActivitySource.StartActivity(
            "coffeeshop.audit.log", ActivityKind.Internal);
        activity?.SetTag("executor.id", this.Id);
        activity?.SetTag("order.id", order.OrderId);
        activity?.SetTag("order.customer", order.CustomerName);
        activity?.SetTag("order.total", order.Total);
        activity?.SetTag("order.items", order.Items.Count);

        try
        {
            await LogAsync(order, ct);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "[Workflow] Audit skipped for order {OrderId}: {Message}", order.OrderId, ex.Message);
        }
    }

    public async Task LogAsync(OrderResult order, CancellationToken ct = default)
    {
        var date = order.PlacedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = Path.Combine(_ordersRoot, date);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"{order.OrderId}.md");
        var total = order.Total.ToString("F2", CultureInfo.InvariantCulture);
        var itemRows = string.Join("\n", order.Items.Select(
            i => string.Create(CultureInfo.InvariantCulture,
                $"| {i.MenuItemName} | {i.Quantity} | ${i.UnitPrice:F2} | ${i.UnitPrice * i.Quantity:F2} |")));

        var content = $"""
            # Order {order.OrderId}

            - **Customer:** {order.CustomerName} ({order.CustomerId})
            - **Placed:** {order.PlacedAt:O}
            - **Total:** ${total}

            ## Items

            | Item | Qty | Unit Price | Subtotal |
            |------|-----|-----------|----------|
            {itemRows}

            ---
            *Logged by AuditAgent*
            """;

        await File.WriteAllTextAsync(path, content, ct);
        _logger.LogInformation("[AuditAgent] Order {OrderId} logged to {Path}", order.OrderId, path);
    }
}
