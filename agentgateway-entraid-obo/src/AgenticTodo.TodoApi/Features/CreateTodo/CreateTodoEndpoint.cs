using System.Net.Http.Json;
using AgenticTodo.ServiceDefaults;
using AgenticTodo.TodoApi.Ports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace AgenticTodo.TodoApi.Features.CreateTodo;

public static class CreateTodoEndpoint
{
    public static RouteGroupBuilder MapCreateTodo(this RouteGroupBuilder group)
    {
        group.MapPost("/", HandleAsync)
            .RequireAuthorization(Extensions.SuperAdminPolicy)
            .WithName("CreateTodo")
            .WithSummary("Create a todo by name; the agent generates its description (SuperAdmin only)")
            .Produces<TodoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    private static async Task<Results<Created<TodoResponse>, ProblemHttpResult>> HandleAsync(
        CreateTodoRequest request,
        IAgentGateway agentGateway,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        using var response = await agentGateway.ForwardAsync(
            HttpMethod.Post, "/agent/todos", JsonContent.Create(request), httpContext, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var problemDetail = await response.Content.ReadAsStringAsync(cancellationToken);
            return TypedResults.Problem(problemDetail, statusCode: (int)response.StatusCode);
        }

        var todo = await response.Content.ReadFromJsonAsync<TodoResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Agent returned no parsable todo.");
        var created = todo with { Message = "Todo created" };
        return TypedResults.Created($"/todos/{created.Id}", created);
    }
}
