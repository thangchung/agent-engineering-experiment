using AgenticTodo.TodoMcpServer.Domain;
using Microsoft.EntityFrameworkCore;

namespace AgenticTodo.TodoMcpServer.Adapters;

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Todo>(entity =>
        {
            entity.HasIndex(todo => todo.OwnerObjectId);
        });
    }
}
