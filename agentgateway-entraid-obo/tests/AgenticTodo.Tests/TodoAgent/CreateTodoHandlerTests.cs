using System.Security.Claims;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;
using AgenticTodo.TodoAgent.Features.CreateTodo;
using Xunit;

namespace AgenticTodo.Tests.TodoAgent;

public class CreateTodoHandlerTests
{
    [Fact]
    public async Task HandleAsync_trims_description_to_40_words()
    {
        var longDescription = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"word{i}"));
        var generator = new FakeDescriptionGenerator(longDescription);
        var sink = new FakeTodoSink();
        var handler = new CreateTodoHandler(generator, sink);

        await handler.HandleAsync("Buy milk", AnonymousUser(), CancellationToken.None);

        Assert.NotNull(sink.LastDescription);
        Assert.Equal(40, sink.LastDescription!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task HandleAsync_keeps_description_under_the_limit_unchanged()
    {
        var shortDescription = "A short todo description.";
        var generator = new FakeDescriptionGenerator(shortDescription);
        var sink = new FakeTodoSink();
        var handler = new CreateTodoHandler(generator, sink);

        await handler.HandleAsync("Buy milk", AnonymousUser(), CancellationToken.None);

        Assert.Equal(shortDescription, sink.LastDescription);
    }

    [Fact]
    public async Task HandleAsync_calls_generator_and_sink_exactly_once()
    {
        var generator = new FakeDescriptionGenerator("A short description.");
        var sink = new FakeTodoSink();
        var handler = new CreateTodoHandler(generator, sink);

        await handler.HandleAsync("Buy milk", AnonymousUser(), CancellationToken.None);

        Assert.Equal(1, generator.CallCount);
        Assert.Equal(1, sink.SaveCallCount);
    }

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());
}

file sealed class FakeDescriptionGenerator(string result) : IDescriptionGenerator
{
    public int CallCount { get; private set; }

    public Task<string> GenerateAsync(string name, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(result);
    }
}

file sealed class FakeTodoSink : ITodoSink
{
    public int SaveCallCount { get; private set; }

    public string? LastDescription { get; private set; }

    public Task<TodoResult> SaveAsync(string name, string description, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        SaveCallCount++;
        LastDescription = description;
        return Task.FromResult(new TodoResult(1, name, description, false, "test-oid"));
    }

    public Task<IReadOnlyList<TodoResult>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TodoResult>>([]);
}
