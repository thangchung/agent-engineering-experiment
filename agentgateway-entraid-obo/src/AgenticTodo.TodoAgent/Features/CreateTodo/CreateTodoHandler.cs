using System.Diagnostics;
using System.Security.Claims;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;
using Microsoft.Identity.Web;

namespace AgenticTodo.TodoAgent.Features.CreateTodo;

public sealed class CreateTodoHandler(IDescriptionGenerator descriptionGenerator, ITodoSink todoSink) : ICreateTodo
{
    private const int MaxDescriptionWords = 40;

    public async Task<TodoResult> HandleAsync(string name, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("todo.owner.oid", user.GetObjectId());

        var generated = await descriptionGenerator.GenerateAsync(name, cancellationToken);
        var description = TrimToWordLimit(generated, MaxDescriptionWords);
        return await todoSink.SaveAsync(name, description, user, cancellationToken);
    }

    private static string TrimToWordLimit(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text : string.Join(' ', words.Take(maxWords));
    }
}
