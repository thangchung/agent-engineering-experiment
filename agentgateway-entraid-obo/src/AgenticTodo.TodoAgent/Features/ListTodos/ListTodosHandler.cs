using System.Security.Claims;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;

namespace AgenticTodo.TodoAgent.Features.ListTodos;

public sealed class ListTodosHandler(ITodoSink todoSink) : IListTodos
{
    public Task<IReadOnlyList<TodoResult>> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        todoSink.ListAsync(user, cancellationToken);
}
