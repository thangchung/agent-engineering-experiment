using System.Security.Claims;

namespace AgenticTodo.TodoAgent.Domain.Ports;

public interface ITodoSink
{
    Task<TodoResult> SaveAsync(string name, string description, ClaimsPrincipal user, CancellationToken cancellationToken);

    Task<IReadOnlyList<TodoResult>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken);
}
