using AgenticTodo.ServiceDefaults;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgenticTodo.TodoAgent.Features.CreateTodo;

public static class CreateTodoEndpoint
{
    public static IEndpointRouteBuilder MapCreateTodo(this IEndpointRouteBuilder app)
    {
        app.MapPost("/agent/todos", HandleAsync)
            .RequireAuthorization(Extensions.SuperAdminPolicy)
            .WithName("CreateAgentTodo")
            .WithSummary("Generate a description for a todo and persist it via MCP (SuperAdmin only)");

        return app;
    }

    private static async Task<Results<Ok<TodoResult>, ProblemHttpResult>> HandleAsync(
        CreateTodoRequest request,
        ICreateTodo createTodo,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var result = await createTodo.HandleAsync(request.Name, httpContext.User, cancellationToken);
            return TypedResults.Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
        }
    }
}
