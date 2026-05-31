using Azure.AI.AgentServer.Invocations;
using Microsoft.Agents.AI;
using System.Text.Json;
using System.Text;

namespace Claw.Api.Agents;

/// <summary>
/// Handles Foundry Hosted Agent invocation requests.
/// Bridges the Foundry Invocations protocol to ClawRuntime session handling.
/// </summary>
public sealed class CoffeeshopInvocationHandler(
    ClawRuntime runtime,
    ILogger<CoffeeshopInvocationHandler> logger) : InvocationHandler
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public override async Task HandleAsync(
        HttpRequest request,
        HttpResponse response,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = ResolveSessionId(request, context.SessionId);
        await HandleCoreAsync(request, response, context.InvocationId, sessionId, cancellationToken);
    }

    public async Task HandleDirectAsync(
        HttpRequest request,
        HttpResponse response,
        string invocationId,
        string sessionId,
        CancellationToken cancellationToken) =>
        await HandleCoreAsync(request, response, invocationId, sessionId, cancellationToken);

    private async Task HandleCoreAsync(
        HttpRequest request,
        HttpResponse response,
        string invocationId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        // Parse input from request body: {"input": "..."} or plain text
        string input;
        try
        {
            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(cancellationToken);

            if (body.TrimStart().StartsWith('{'))
            {
                using var doc = JsonDocument.Parse(body);
                input = doc.RootElement.TryGetProperty("input", out var inputEl)
                    ? inputEl.GetString() ?? body
                    : body;
            }
            else
            {
                input = body;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Invocation] Failed to parse request body");
            response.StatusCode = StatusCodes.Status400BadRequest;
            await response.WriteAsync("Invalid request body", cancellationToken);
            return;
        }

        logger.LogInformation("[Invocation] Received input length={Len}", input.Length);

        try
        {
            logger.LogInformation("[Invocation] Using sessionId={SessionId}", sessionId);

            var result = await runtime.HandleAsync(sessionId, input, cancellationToken);
            logger.LogInformation("[Invocation] Completed response length={Len}", result.Length);

            response.ContentType = "text/plain";
            await response.WriteAsync(result, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("[Invocation] Request canceled by caller");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Invocation] Workflow execution failed");
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsync($"Error: {ex.Message}", cancellationToken);
        }
    }

    private static string ResolveSessionId(HttpRequest request, string fallback)
    {
        if (request.Query.TryGetValue("agent_session_id", out var querySessionId)
            && !string.IsNullOrWhiteSpace(querySessionId))
        {
            return querySessionId.ToString();
        }

        return !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : $"invocation:{Guid.NewGuid():N}";
    }
}
