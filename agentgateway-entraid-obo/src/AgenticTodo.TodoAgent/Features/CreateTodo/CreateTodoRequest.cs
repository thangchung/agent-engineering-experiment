using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgenticTodo.TodoAgent.Features.CreateTodo;

public sealed record CreateTodoRequest(
    [property: Required, Description("Human-supplied todo name")] string Name);
