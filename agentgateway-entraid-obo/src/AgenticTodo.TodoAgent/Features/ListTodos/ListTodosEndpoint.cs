using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgenticTodo.TodoAgent.Features.ListTodos;

public static class ListTodosEndpoint
{
    public static IEndpointRouteBuilder MapListTodos(this IEndpointRouteBuilder app)
    {
        app.MapGet("/agent/todos", HandleAsync)
            .RequireAuthorization()
            .WithName("ListAgentTodos")
            .WithSummary("List todos for the authenticated user via MCP");

        return app;
    }

    private static async Task<Ok<IReadOnlyList<TodoResult>>> HandleAsync(
        IListTodos listTodos,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await listTodos.HandleAsync(httpContext.User, cancellationToken);
        return TypedResults.Ok(result);
    }
}
