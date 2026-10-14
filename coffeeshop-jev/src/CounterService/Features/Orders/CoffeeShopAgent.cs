using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AGUI.Server;
using CounterService.Common;
using CounterService.Domain;
using CounterService.Features.Orders.Common;
using CounterService.Features.Orders.Workflow;
using DecisionModel.Client;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CounterService.Features.Orders;

public sealed record GateInfo(string Decision, string Intent, double Confidence, double OnMenu, string Model);

/// <summary>The AG-UI shared state (research.md §17.3). Clients echo back CustomerId, Asks and History only.</summary>
public sealed record OrderUiState
{
    public string? CustomerId { get; init; }

    public int Asks { get; init; }

    public IReadOnlyList<string> History { get; init; } = [];

    public string Status { get; init; } = "Idle";

    public GateInfo? Gate { get; init; }

    public IReadOnlyList<OrderLine> Lines { get; init; } = [];

    public IReadOnlyList<StationTicket> Tickets { get; init; } = [];

    public decimal Total { get; init; }

    public int? OrderNumber { get; init; }

    public string? Message { get; init; }
}

/// <summary>
/// AG-UI front door (research.md §17.3): runs one <see cref="OrderWorkflow"/> per AG-UI run and
/// translates its events. MAF .NET cannot expose workflow interrupts over AG-UI, so clarify is a
/// turn boundary: the question ends the run and the answer arrives as the next run, carrying
/// Asks/History in the client-echoed state.
/// </summary>
public sealed partial class CoffeeShopAgent(
    DecisionModelClient decisionModel,
    ICatalogClient catalog,
    AIAgent counterAgent,
    AIAgent baristaAgent,
    AIAgent kitchenAgent,
    OrderStore store,
    DecisionProfile profile,
    ILogger<CoffeeShopAgent> logger) : AIAgent
{
    public const string ProgressTool = "order_progress";
    public const string MenuTool = "show_menu";
    public const string HistoryTool = "show_history";

    private const int MaxTextLength = 500;
    private const int MaxHistory = IntentPolicy.MaxAsks + 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex CustomerIdPattern();

    protected override ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default) =>
        new(new FrontDoorSession());

    protected override ValueTask<JsonElement> SerializeSessionCoreAsync(AgentSession session, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(JsonSerializer.SerializeToElement(new { }));

    protected override ValueTask<AgentSession> DeserializeSessionCoreAsync(JsonElement serializedState, JsonSerializerOptions? jsonSerializerOptions = null, CancellationToken cancellationToken = default) =>
        new(new FrontDoorSession());

    protected override Task<AgentResponse> RunCoreAsync(IEnumerable<ChatMessage> messages, AgentSession? session = null, AgentRunOptions? options = null, CancellationToken cancellationToken = default) =>
        RunCoreStreamingAsync(messages, session, options, cancellationToken).ToAgentResponseAsync(cancellationToken);

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var latest = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text?.Trim() ?? "";
        var state = ReadClientState(options);

        if (latest.Length is 0 or > MaxTextLength)
        {
            yield return Text("Please keep your message between 1 and 500 characters.");
            yield break;
        }

        IReadOnlyList<MenuItem>? menu = null;
        try
        {
            menu = await catalog.GetMenuAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogUnavailableException)
        {
        }

        if (menu is null)
        {
            yield return Text("We can't take orders right now, please try again in a moment.");
            yield break;
        }

        var trace = new RunTrace();
        trace.AgUi("RUN_STARTED", $"customer {state.CustomerId ?? "-"}, asks {state.Asks}, {state.History.Count} earlier message(s)");
        trace.Audit("message", latest);
        trace.Audit("provider", $"{profile.Name} / {profile.Model}");

        var workflow = OrderWorkflow.Build(decisionModel, menu, counterAgent, baristaAgent, kitchenAgent,
            state.CustomerId, store, new OrderState(state.History, state.Asks), turnBoundary: true, profile);

        var working = state with { Status = "Working", Gate = null, Lines = [], Tickets = [], Total = 0, OrderNumber = null, Message = null };
        yield return trace.Emit(Snapshot(working), "STATE_SNAPSHOT", "status Working");

        await using var run = await InProcessExecution.RunStreamingAsync(workflow, latest, cancellationToken: cancellationToken).ConfigureAwait(false);
        var failed = false;

        await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (evt)
            {
                case GateDecided gate:
                    var data = JsonSerializer.SerializeToElement(gate.Data, Json);
                    working = working with
                    {
                        Gate = new GateInfo(
                            data.GetProperty("decisionType").GetString()!,
                            data.GetProperty("intentChoice").GetString()!,
                            data.GetProperty("intentConfidence").GetDouble(),
                            data.GetProperty("onMenuNoul").GetDouble(),
                            data.GetProperty("modelName").GetString() ?? ""),
                    };
                    trace.Audit("gate", $"{working.Gate.Decision} · intent {working.Gate.Intent} {working.Gate.Confidence:0.00} · on menu {working.Gate.OnMenu:0.00} · model {working.Gate.Model}");
                    yield return trace.Emit(Snapshot(working), "STATE_SNAPSHOT", $"gate {working.Gate.Decision}");
                    break;

                case GateFailed failure:
                    trace.Audit("gate-error", $"{profile.Name} / {profile.Model}: {failure.Data}");
                    logger.LogWarning("Decision model failed at the Gate ({Provider} / {Model}): {Detail}", profile.Name, profile.Model, failure.Data);
                    break;

                case SplitDone split when split.Data is IReadOnlyList<OrderLine> lines:
                    working = working with { Lines = lines };
                    trace.Audit("split", RunTrace.Describe(lines));
                    yield return trace.Emit(Snapshot(working), "STATE_SNAPSHOT", $"{lines.Count} line(s) assigned");
                    break;

                case WorkflowOutputEvent output when output.GetType() == typeof(WorkflowOutputEvent):
                    foreach (var update in Finish(output.Data, working, latest, trace))
                    {
                        yield return update;
                    }

                    break;

                case WorkflowErrorEvent or ExecutorFailedEvent:
                    failed = true;
                    break;
            }
        }

        if (failed)
        {
            trace.AgUi("RUN_ERROR", "the order workflow failed");
            throw new InvalidOperationException("The order workflow failed.");
        }
    }

    private IEnumerable<AgentResponseUpdate> Finish(object? output, OrderUiState working, string latest, RunTrace trace)
    {
        var fresh = working with { Asks = 0, History = [] };

        switch (output)
        {
            case ClarifyRequest when working.Asks >= IntentPolicy.MaxAsks:
                // Extract can fail on a message the Gate accepted, and only Gate questions were counted: cap them here too.
                foreach (var update in Finish(OrderResult.Rejected(RejectReason.AskCap, "I'm sorry, I still couldn't understand your order. Please try again."), working, latest, trace))
                {
                    yield return update;
                }

                break;

            case ClarifyRequest ask:
                trace.Audit("clarify", ask.Question);
                yield return trace.Emit(Snapshot(working with
                {
                    Status = "Asking",
                    Asks = working.Asks + 1,
                    History = [.. working.History, latest],
                    Message = ask.Question,
                }), "STATE_SNAPSHOT", $"status Asking ({working.Asks + 1} of {IntentPolicy.MaxAsks})");
                yield return trace.Emit(Text(ask.Question), "TEXT_MESSAGE_CONTENT", ask.Question);
                break;

            case MenuResponse menu:
                yield return trace.Emit(Snapshot(fresh with { Status = "Idle" }), "STATE_SNAPSHOT", "status Idle");
                yield return trace.Emit(Text("Here's what we have today."), "TEXT_MESSAGE_CONTENT", "Here's what we have today.");
                yield return trace.Emit(ToolCard(MenuTool, menu.Items.Select(m => new { m.Id, name = m.DisplayName, price = m.PriceUsd, station = m.Station })), "TOOL_CALL_RESULT", $"{MenuTool}: {menu.Items.Count} items");

                break;

            case HistoryResponse history:
                var done = history.Orders.Where(o => o.Result.Status == OrderStatus.Completed).ToList();
                yield return trace.Emit(Snapshot(fresh with { Status = "Idle" }), "STATE_SNAPSHOT", "status Idle");
                if (done.Count == 0)
                {
                    yield return trace.Emit(Text("You haven't ordered anything yet. Want to see the menu?"), "TEXT_MESSAGE_CONTENT", "empty history");
                }
                else
                {
                    yield return trace.Emit(Text("Here's what you've ordered, newest first."), "TEXT_MESSAGE_CONTENT", "history intro");
                    yield return trace.Emit(ToolCard(HistoryTool, done.Take(10)), "TOOL_CALL_RESULT", $"{HistoryTool}: {Math.Min(done.Count, 10)} orders");
                }

                break;

            case OrderResult result:
                var number = store.NextNumber();
                trace.Audit("result", $"{result.Status}{(result.Reason is { } why ? $" ({why})" : "")} · total {result.Total:0.00} · {result.Message}");
                foreach (var ticket in result.Tickets)
                {
                    trace.Audit("ticket", $"{ticket.Station}{(ticket.IsFallback ? " (fallback)" : "")}: {string.Join(", ", ticket.Items.Select(i => $"{i.Qty}× {i.Name}"))}");
                }

                // Stored before the last events are streamed: a browser that disconnects mid-stream must not lose the order.
                var final = Snapshot(fresh with
                {
                    Status = result.Status.ToString(),
                    Lines = result.Lines.Count > 0 ? result.Lines : working.Lines,
                    Tickets = result.Tickets,
                    Total = result.Total,
                    OrderNumber = number,
                    Message = result.Message,
                });
                trace.AgUi("STATE_SNAPSHOT", $"status {result.Status}, order #{number}");
                trace.AgUi("TEXT_MESSAGE_CONTENT", result.Message);
                trace.AgUi("RUN_FINISHED", $"order #{number} {result.Status}");
                store.Add(result, working.CustomerId, trace.Build([.. working.History, latest], profile.Name, profile.Model), number);
                yield return final;
                yield return Text(result.Message);
                break;
        }
    }

    private static OrderUiState ReadClientState(AgentRunOptions? options)
    {
        if (options is not ChatClientAgentRunOptions { ChatOptions: { } chatOptions }
            || !chatOptions.TryGetRunAgentInput(out var input)
            || input?.State is not { ValueKind: JsonValueKind.Object } raw)
        {
            return new OrderUiState();
        }

        // The client echo is untrusted input: take three fields, clamp every one.
        var customerId = raw.TryGetProperty("customerId", out var c) && c.ValueKind == JsonValueKind.String && CustomerIdPattern().IsMatch(c.GetString()!)
            ? c.GetString()
            : null;
        var asks = raw.TryGetProperty("asks", out var a) && a.TryGetInt32(out var n) ? Math.Clamp(n, 0, IntentPolicy.MaxAsks) : 0;
        var history = raw.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array
            ? h.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!)
                .Where(s => s.Length is > 0 and <= MaxTextLength).TakeLast(MaxHistory).ToList()
            : [];

        return new OrderUiState { CustomerId = customerId, Asks = asks, History = history };
    }

    private static AgentResponseUpdate Text(string text) =>
        new(ChatRole.Assistant, text) { MessageId = Guid.NewGuid().ToString("N") };

    private static AgentResponseUpdate Snapshot(OrderUiState state) => ToolCard(ProgressTool, state);

    private static AgentResponseUpdate ToolCard(string tool, object payload)
    {
        var callId = Guid.NewGuid().ToString("N");
        return new AgentResponseUpdate(
            ChatRole.Assistant,
            [new FunctionCallContent(callId, tool), new FunctionResultContent(callId, JsonSerializer.SerializeToElement(payload, Json))])
        {
            MessageId = Guid.NewGuid().ToString("N"),
        };
    }

    private sealed class FrontDoorSession : AgentSession;
}
