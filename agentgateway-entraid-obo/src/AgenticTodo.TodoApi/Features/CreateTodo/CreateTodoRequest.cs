using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace AgenticTodo.TodoApi.Features.CreateTodo;

public sealed record CreateTodoRequest(
    [property: Required, Description("Name of the todo to create")] string Name);
