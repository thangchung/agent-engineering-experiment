namespace AgenticTodo.TodoAgent.Domain;

public sealed record TodoResult(int Id, string Name, string Description, bool Checked, string UserId);
