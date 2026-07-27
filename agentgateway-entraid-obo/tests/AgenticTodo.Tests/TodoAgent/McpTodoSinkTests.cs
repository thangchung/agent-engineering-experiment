using System.ComponentModel;
using System.Security.Claims;
using AgenticTodo.TodoAgent.Adapters.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Abstractions;
using ModelContextProtocol.Server;
using Xunit;

namespace AgenticTodo.Tests.TodoAgent;

public sealed class McpTodoSinkTests : IAsyncLifetime
{
    private WebApplication stubServer = null!;
    private string endpoint = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<StubTodoTools>();

        stubServer = builder.Build();
        stubServer.MapMcp();
        await stubServer.StartAsync();

        var addresses = stubServer.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses;
        endpoint = addresses.First();
    }

    public async Task DisposeAsync()
    {
        await stubServer.StopAsync();
        await stubServer.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_sends_name_description_checked_and_parses_the_result()
    {
        var sink = new McpTodoSink(new FakeAuthorizationHeaderProvider(), BuildConfiguration(endpoint), NullLogger<McpTodoSink>.Instance, new FakeHostEnvironment());

        var result = await sink.SaveAsync("Buy milk", "Purchase milk.", AnonymousUser(), CancellationToken.None);

        Assert.Equal("Buy milk", result.Name);
        Assert.Equal("Purchase milk.", result.Description);
        Assert.False(result.Checked);
        Assert.Equal("stub-oid", result.UserId);
    }

    [Fact]
    public async Task ListAsync_parses_the_returned_list()
    {
        var sink = new McpTodoSink(new FakeAuthorizationHeaderProvider(), BuildConfiguration(endpoint), NullLogger<McpTodoSink>.Instance, new FakeHostEnvironment());

        var results = await sink.ListAsync(AnonymousUser(), CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("stub todo", results[0].Name);
    }

    private static IConfiguration BuildConfiguration(string mcpEndpoint) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mcp:ClientId"] = "stub-mcp-client-id",
                ["AgentIdentity:AgentIdentityId"] = "stub-agent-identity-id",
                ["Mcp:Endpoint"] = mcpEndpoint,
            })
            .Build();

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity());
}

file sealed class FakeAuthorizationHeaderProvider : IAuthorizationHeaderProvider
{
    public Task<string> CreateAuthorizationHeaderAsync(
        IEnumerable<string> scopes,
        AuthorizationHeaderProviderOptions? options = null,
        ClaimsPrincipal? claimsPrincipal = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Bearer stub-app-token");

    public Task<string> CreateAuthorizationHeaderForAppAsync(
        string scopes,
        AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Bearer stub-app-token");

    public Task<string> CreateAuthorizationHeaderForUserAsync(
        IEnumerable<string> scopes,
        AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
        ClaimsPrincipal? claimsPrincipal = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Bearer stub-user-token");
}

[McpServerToolType]
file sealed class StubTodoTools
{
    [McpServerTool(Name = "create_todo"), Description("Stub create_todo for adapter tests.")]
    public static StubTodoResult CreateTodo(string name, string description, bool @checked) =>
        new(1, name, description, @checked, "stub-oid");

    [McpServerTool(Name = "list_todos"), Description("Stub list_todos for adapter tests.")]
    public static IReadOnlyList<StubTodoResult> ListTodos() =>
        [new StubTodoResult(1, "stub todo", "a stub description", false, "stub-oid")];
}

file sealed record StubTodoResult(int Id, string Name, string Description, bool Checked, string UserId);

file sealed class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "AgenticTodo.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
