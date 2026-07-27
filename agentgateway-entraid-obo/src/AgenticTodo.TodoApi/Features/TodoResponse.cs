namespace AgenticTodo.TodoApi.Features;

public sealed record TodoResponse(int Id, string Name, string Description, bool Checked, string UserId, string? Message = null);
