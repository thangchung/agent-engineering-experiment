using AgenticTodo.TodoMcpServer.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgenticTodo.TodoMcpServer.Adapters;

public sealed class EfTodoRepository(TodoDbContext db) : ITodoRepository
{
    public async Task<Todo> AddAsync(string name, string description, bool @checked, string ownerObjectId, CancellationToken cancellationToken)
    {
        var todo = new Todo
        {
            Name = name,
            Description = description,
            Checked = @checked,
            OwnerObjectId = ownerObjectId,
        };
        db.Todos.Add(todo);
        await db.SaveChangesAsync(cancellationToken);
        return todo;
    }

    public async Task<IReadOnlyList<Todo>> ListAsync(string ownerObjectId, CancellationToken cancellationToken) =>
        await db.Todos
            .Where(todo => todo.OwnerObjectId == ownerObjectId)
            .OrderBy(todo => todo.Id)
            .ToListAsync(cancellationToken);
}
