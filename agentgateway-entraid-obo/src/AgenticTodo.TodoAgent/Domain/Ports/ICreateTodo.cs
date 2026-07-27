using System.Security.Claims;

namespace AgenticTodo.TodoAgent.Domain.Ports;

public interface ICreateTodo
{
    Task<TodoResult> HandleAsync(string name, ClaimsPrincipal user, CancellationToken cancellationToken);
}
