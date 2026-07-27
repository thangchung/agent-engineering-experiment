using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using AgenticTodo.TodoAgent.Domain;
using AgenticTodo.TodoAgent.Domain.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgenticTodo.TodoAgent.Adapters.Mcp;

public sealed class McpTodoSink(
    IAuthorizationHeaderProvider authorizationHeaderProvider,
    IConfiguration configuration,
    ILogger<McpTodoSink> logger,
    IHostEnvironment environment) : ITodoSink
{
    public async Task<TodoResult> SaveAsync(string name, string description, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("gen_ai.tool.name", "create_todo");

        await using var client = await CreateClientAsync(user, cancellationToken);
        var result = await client.CallToolAsync(
            "create_todo",
            new Dictionary<string, object?>
            {
                ["name"] = name,
                ["description"] = description,
                ["checked"] = false,
            },
            cancellationToken: cancellationToken);

        var payload = ExtractTextContent(result);
        return JsonSerializer.Deserialize<TodoResult>(payload, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("MCP create_todo returned no parsable result.");
    }

    public async Task<IReadOnlyList<TodoResult>> ListAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("gen_ai.tool.name", "list_todos");

        await using var client = await CreateClientAsync(user, cancellationToken);
        var result = await client.CallToolAsync("list_todos", cancellationToken: cancellationToken);

        var payload = ExtractTextContent(result);
        return JsonSerializer.Deserialize<List<TodoResult>>(payload, JsonSerializerOptions.Web) ?? [];
    }

    private async Task<McpClient> CreateClientAsync(ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        var mcpClientId = configuration["Mcp:ClientId"]
            ?? throw new InvalidOperationException("Mcp:ClientId is required.");
        var agentIdentityId = configuration["AgentIdentity:AgentIdentityId"]
            ?? throw new InvalidOperationException("AgentIdentity:AgentIdentityId is required.");
        var endpoint = configuration["Mcp:Endpoint"]
            ?? throw new InvalidOperationException("Mcp:Endpoint is required.");

        // Hop-2 Agent-ID OBO: the agent's own identity acting on behalf of the human user.
        var header = await authorizationHeaderProvider.CreateAuthorizationHeaderForUserAsync(
            [$"api://{mcpClientId}/access_as_user"],
            new AuthorizationHeaderProviderOptions().WithAgentIdentity(agentIdentityId),
            user,
            cancellationToken);

        // Development-only: the check is hard-coded here, not left to a caller/config flag,
        // so raw-header logging can't be accidentally enabled outside Development.
        if (environment.IsDevelopment())
        {
            logger.LogInformation("Hop-2 Agent-ID OBO exchanged header: {Header}", header);
        }

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = header },
        });

        return await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static string ExtractTextContent(CallToolResult result) =>
        result.Content?
            .OfType<TextContentBlock>()
            .Select(block => block.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text))
        ?? string.Empty;
}
