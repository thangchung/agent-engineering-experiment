using CounterService.Features.Orders.Common;

namespace CounterService.Features.Orders;

public sealed record AnswerRequest(string Text);

public static class AnswerClarificationCommand
{
    public static IEndpointRouteBuilder MapAnswerClarification(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orders/{runId}/answer", Handle)
            .WithName("AnswerClarification")
            .WithSummary("Answers the question the order is currently waiting on.");

        return app;
    }

    private static IResult Handle(string runId, AnswerRequest request, RunRegistry runs)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Results.BadRequest(new { error = "text must not be empty" });
        }

        return runs.TrySubmitAnswer(runId, request.Text) switch
        {
            SubmitAnswerResult.Ok => Results.Accepted(),
            SubmitAnswerResult.NotFound => Results.NotFound(),
            SubmitAnswerResult.NotPending => Results.Conflict(new { error = "this order is not waiting for an answer" }),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
