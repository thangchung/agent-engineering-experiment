using Azure.AI.AgentServer.Invocations;
using Microsoft.Agents.AI;
using System.Text.Json;
using System.Text;

namespace Claw.Api.Agents;

/// <summary>
/// Handles Foundry Hosted Agent invocation requests.
/// Bridges the Foundry Invocations protocol to CoffeeshopWorkflow.
/// </summary>
public sealed class CoffeeshopInvocationHandler(
    CoffeeshopWorkflow workflow,
    ILogger<CoffeeshopInvocationHandler> logger) : InvocationHandler
{
    private static readonly JsonSerializerOptions _jsonOpts = new(JsonSerializerDefaults.Web);

    public override async Task HandleAsync(
        HttpRequest request,
        HttpResponse response,
        InvocationContext context,
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
            // Create a new session per invocation (stateless protocol)
            var session = await workflow.CreateSessionAsync(cancellationToken);

            var sb = new StringBuilder();
            await foreach (var update in workflow.RunStreamingAsync(input, session, cancellationToken))
            {
                if (update.Text is { Length: > 0 } text)
                    sb.Append(text);
            }

            var result = sb.ToString();
            logger.LogInformation("[Invocation] Completed response length={Len}", result.Length);

            response.ContentType = "text/plain";
            await response.WriteAsync(result, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Invocation] Workflow execution failed");
            response.StatusCode = StatusCodes.Status500InternalServerError;
            await response.WriteAsync($"Error: {ex.Message}", cancellationToken);
        }
    }
}
