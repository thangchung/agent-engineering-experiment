using AgenticTodo.TodoMcpServer.Adapters;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticTodo.Tests.TodoMcpServer;

public sealed class EfTodoRepositoryTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly TodoDbContext dbContext;
    private readonly EfTodoRepository repository;

    public EfTodoRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TodoDbContext>()
            .UseSqlite(connection)
            .Options;

        dbContext = new TodoDbContext(options);
        dbContext.Database.EnsureCreated();
        repository = new EfTodoRepository(dbContext);
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task AddAsync_persists_todo_with_owner()
    {
        var todo = await repository.AddAsync("Buy milk", "Purchase milk.", false, "owner-1", CancellationToken.None);

        Assert.True(todo.Id > 0);
        Assert.Equal("owner-1", todo.OwnerObjectId);
        Assert.Equal("Buy milk", todo.Name);
    }

    [Fact]
    public async Task ListAsync_returns_only_the_requested_owners_todos()
    {
        await repository.AddAsync("Buy milk", "desc", false, "owner-1", CancellationToken.None);
        await repository.AddAsync("Buy bread", "desc", false, "owner-2", CancellationToken.None);

        var todos = await repository.ListAsync("owner-1", CancellationToken.None);

        var todo = Assert.Single(todos);
        Assert.Equal("Buy milk", todo.Name);
    }

    [Fact]
    public async Task ListAsync_orders_by_id_ascending()
    {
        await repository.AddAsync("first", "desc", false, "owner-1", CancellationToken.None);
        await repository.AddAsync("second", "desc", false, "owner-1", CancellationToken.None);

        var todos = await repository.ListAsync("owner-1", CancellationToken.None);

        Assert.Equal(["first", "second"], todos.Select(todo => todo.Name));
    }
}
