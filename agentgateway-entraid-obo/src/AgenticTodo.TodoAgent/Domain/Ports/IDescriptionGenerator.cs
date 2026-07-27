namespace AgenticTodo.TodoAgent.Domain.Ports;

public interface IDescriptionGenerator
{
    Task<string> GenerateAsync(string name, CancellationToken cancellationToken);
}
