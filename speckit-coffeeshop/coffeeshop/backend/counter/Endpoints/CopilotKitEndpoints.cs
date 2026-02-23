using Microsoft.Agents.Builder;
using Microsoft.Agents.Hosting.AspNetCore;

namespace CoffeeShop.Counter.Endpoints;

/// <summary>
/// <c>POST /api/v1/copilotkit</c> — AG-UI streaming endpoint.
///
/// Receives a CopilotKit runtime request, runs CounterAgent via MAF
/// IAgentHttpAdapter.ProcessAsync which handles the full AG-UI SSE protocol
/// including streaming tool-call events and text deltas.
///
/// Response Content-Type is set to text/event-stream by the adapter.
/// Do not buffer — the adapter pipes through directly.
/// </summary>
public static class CopilotKitEndpoints
{
    public static IEndpointRouteBuilder MapCopilotKitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/copilotkit", ProcessCopilotKitRequest)
           .WithName("CopilotKitStream")
           .WithOpenApi()
           .ExcludeFromDescription(); // Hide from OpenAPI docs (internal protocol endpoint)

        return app;
    }

    private static async Task ProcessCopilotKitRequest(
        HttpRequest request,
        HttpResponse response,
        IAgentHttpAdapter adapter,
        IAgent agent,
        CancellationToken ct)
    {
        await adapter.ProcessAsync(request, response, agent, ct);
    }
}
