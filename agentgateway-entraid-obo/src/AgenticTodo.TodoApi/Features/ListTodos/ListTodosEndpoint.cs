using System.Net.Http.Json;
using AgenticTodo.TodoApi.Ports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgenticTodo.TodoApi.Features.ListTodos;

public static class ListTodosEndpoint
{
    public static RouteGroupBuilder MapListTodos(this RouteGroupBuilder group)
    {
        group.MapGet("/", HandleAsync)
            .WithName("ListTodos")
            .WithSummary("List all todos for the signed-in user")
            .Produces<IReadOnlyList<TodoResponse>>();

        return group;
    }

    private static async Task<Results<Ok<IReadOnlyList<TodoResponse>>, ProblemHttpResult>> HandleAsync(
        IAgentGateway agentGateway,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        using var response = await agentGateway.ForwardAsync(HttpMethod.Get, "/agent/todos", null, httpContext, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var problemDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            return TypedResults.Problem(problemDetail, statusCode: (int)response.StatusCode);
        }

        var todos = await response.Content.ReadFromJsonAsync<List<TodoResponse>>(cancellationToken) ?? [];
        return TypedResults.Ok<IReadOnlyList<TodoResponse>>(todos);
    }
}
