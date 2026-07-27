namespace AgenticTodo.TodoMcpServer.Domain;

public interface ITodoRepository
{
    Task<Todo> AddAsync(string name, string description, bool @checked, string ownerObjectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Todo>> ListAsync(string ownerObjectId, CancellationToken cancellationToken);
}
