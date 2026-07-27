namespace AgenticTodo.TodoMcpServer.Domain;

public sealed class Todo
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public bool Checked { get; set; }
    public required string OwnerObjectId { get; set; }
}
