namespace AgenticTodo.TodoMcpServer.Tools;

public sealed record TodoResponse(int Id, string Name, string Description, bool Checked, string UserId);
