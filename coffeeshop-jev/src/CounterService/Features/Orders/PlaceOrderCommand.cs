using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CounterService.Common;
using CounterService.Domain;
using CounterService.Features.Orders.Agents;
using CounterService.Features.Orders.Common;
using CounterService.Features.Orders.Workflow;
using Jev.Client;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;

namespace CounterService.Features.Orders;

public sealed record PlaceOrderRequest(string Text);

public static class PlaceOrderCommand
{
    private const int MaxTextLength = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEndpointRouteBuilder MapPlaceOrder(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", Handle)
            .WithName("PlaceOrder")
            .WithSummary("Starts an order. Streams progress as Server-Sent Events.");

        return app;
    }

    private static IResult Handle(
        PlaceOrderRequest request,
        JevClient jev,
        ICatalogClient catalog,
        [FromKeyedServices(AgentKeys.Counter)] AIAgent counterAgent,
        [FromKeyedServices(AgentKeys.Barista)] AIAgent baristaAgent,
        [FromKeyedServices(AgentKeys.Kitchen)] AIAgent kitchenAgent,
        RunRegistry runs,
        OrderStore store,
        HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaxTextLength)
        {
            return Results.BadRequest(new { error = "text must be 1-500 characters" });
        }

        var stream = StreamAsync(request.Text, jev, catalog, counterAgent, baristaAgent, kitchenAgent, runs, store, http.RequestAborted);
        return TypedResults.ServerSentEvents(stream);
    }

    private static async IAsyncEnumerable<SseItem<string>> StreamAsync(
        string text,
        JevClient jev,
        ICatalogClient catalog,
        AIAgent counterAgent,
        AIAgent baristaAgent,
        AIAgent kitchenAgent,
        RunRegistry runs,
        OrderStore store,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var runId = runs.Register();
        yield return Sse(new { runId }, "run");

        IReadOnlyList<MenuItem>? menu = null;
        CatalogUnavailableException? catalogError = null;
        try
        {
            menu = await catalog.GetMenuAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CatalogUnavailableException ex)
        {
            catalogError = ex;
        }

        if (catalogError is not null || menu is null)
        {
            yield return Sse(new { message = "We can't take orders right now, please try again in a moment." }, "error");
            runs.Remove(runId);
            yield break;
        }

        var workflow = OrderWorkflow.Build(jev, menu, counterAgent, baristaAgent, kitchenAgent);

        StreamingRun? run = null;
        var errorSent = false;
        try
        {
            run = await InProcessExecution.RunStreamingAsync(workflow, text, cancellationToken: cancellationToken).ConfigureAwait(false);

            await foreach (var evt in run.WatchStreamAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case GateDecided gate:
                        yield return Sse(gate.Data, "gate");
                        break;

                    case SplitDone split:
                        yield return Sse(split.Data, "split");
                        break;

                    case RequestInfoEvent requestInfo:
                        runs.MarkPending(runId);
                        requestInfo.Request.TryGetDataAs<ClarifyRequest>(out var clarify);
                        yield return Sse(clarify, "ask");

                        var answer = await runs.WaitForAnswerAsync(runId, cancellationToken).ConfigureAwait(false);
                        await run.SendResponseAsync(requestInfo.Request.CreateResponse(answer)).ConfigureAwait(false);
                        break;

                    // AgentResponseEvent is itself a WorkflowOutputEvent subclass - match the
                    // exact type only (research.md T14 finding).
                    case WorkflowOutputEvent output when output.GetType() == typeof(WorkflowOutputEvent):
                        if (output.Data is MenuResponse menuResponse)
                        {
                            yield return Sse(menuResponse.Items, "menu");
                        }
                        else if (output.Data is OrderResult result)
                        {
                            store.Add(result);
                            yield return Sse(result, "done");
                        }

                        break;

                    // Both a WorkflowErrorEvent and an ExecutorFailedEvent can fire for the same
                    // underlying failure - only tell the client once.
                    case WorkflowErrorEvent or ExecutorFailedEvent when !errorSent:
                        errorSent = true;
                        yield return Sse(new { message = "Something went wrong, please try again." }, "error");
                        break;
                }
            }
        }
        finally
        {
            runs.Remove(runId);
            if (run is not null)
            {
                await run.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static SseItem<string> Sse(object? data, string eventType) =>
        new(JsonSerializer.Serialize(data, Json), eventType);
}
