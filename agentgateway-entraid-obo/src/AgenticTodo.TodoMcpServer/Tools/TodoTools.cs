using System.ComponentModel;
using AgenticTodo.TodoMcpServer.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Web;
using ModelContextProtocol.Server;

namespace AgenticTodo.TodoMcpServer.Tools;

[McpServerToolType]
public sealed class TodoTools(ITodoRepository repository, IHttpContextAccessor httpContextAccessor, IConfiguration configuration)
{
    [McpServerTool(Name = "create_todo"), Description("Persist a new todo for the authenticated user. SuperAdmin group membership required.")]
    public async Task<TodoResponse> CreateTodoAsync(
        [Description("Human-supplied todo name")] string name,
        [Description("Agent-generated description")] string description,
        [Description("Completion state")] bool @checked,
        CancellationToken cancellationToken)
    {
        var ownerObjectId = GetOwnerObjectId();
        RequireSuperAdmin();
        var todo = await repository.AddAsync(name, description, @checked, ownerObjectId, cancellationToken);
        return new TodoResponse(todo.Id, todo.Name, todo.Description, todo.Checked, todo.OwnerObjectId);
    }

    [McpServerTool(Name = "list_todos"), Description("List all todos for the authenticated user.")]
    public async Task<IReadOnlyList<TodoResponse>> ListTodosAsync(CancellationToken cancellationToken)
    {
        var ownerObjectId = GetOwnerObjectId();
        var todos = await repository.ListAsync(ownerObjectId, cancellationToken);
        return todos.Select(todo => new TodoResponse(todo.Id, todo.Name, todo.Description, todo.Checked, todo.OwnerObjectId)).ToList();
    }

    private string GetOwnerObjectId() =>
        httpContextAccessor.HttpContext?.User.GetObjectId()
            ?? throw new UnauthorizedAccessException("No authenticated user oid claim available for this MCP call.");

    // Last line of defense before persistence -- checked independently, not trusted from upstream.
    private void RequireSuperAdmin()
    {
        var superAdminGroupId = configuration["Authorization:SuperAdminGroupId"]
            ?? throw new InvalidOperationException("Authorization:SuperAdminGroupId is required.");
        var isSuperAdmin = httpContextAccessor.HttpContext?.User.Claims
            .Any(claim => claim.Type == "groups" && claim.Value == superAdminGroupId) ?? false;
        if (!isSuperAdmin)
        {
            throw new UnauthorizedAccessException("You do not have permission to perform this action. Contact application admin to request access.");
        }
    }
}
