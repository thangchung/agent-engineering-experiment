// CheckerA2AEndpoint — P3 A2A endpoint wrapping IChecker.
// Deployed as a separate process; Executor calls it over A2A instead of in-proc.
using LoopRuntime.Contracts;

namespace LoopRuntime.Checker;

/// <summary>
/// Minimal A2A-compatible review endpoint.
/// The Executor (in its DelegateLoopEvaluator) posts a ReviewRequest here and
/// receives a Verdict back. The business logic stays in CheckerAgent unchanged.
/// </summary>
public static class CheckerA2AEndpoint
{
    public static IEndpointConventionBuilder MapCheckerEndpoints(this WebApplication app)
    {
        // A2A message endpoint — receives the review task, returns a Verdict.
        // Full A2A task lifecycle (TaskSend, TaskGet, cancel) lives in the A2A package
        // routing layer above this; this handler is the terminal business logic.
        return app.MapPost("/checker/review", async (
            CheckerReviewRequest request,
            IChecker checker,
            CancellationToken cancellationToken) =>
        {
            var verdict = await checker
                .ReviewAsync(request.SessionId, request.Path, cancellationToken)
                .ConfigureAwait(false);
            return Results.Ok(verdict);
        });
    }
}

/// <summary>Request payload for the /checker/review endpoint.</summary>
internal sealed record CheckerReviewRequest(string SessionId, string Path);
