using System.Security.Claims;

namespace AgenticTodo.TodoAgent.Domain.Ports;

public interface IListTodos
{
    Task<IReadOnlyList<TodoResult>> HandleAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
