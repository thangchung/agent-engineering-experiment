using System.Security.Claims;
using LoopRuntime.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Abstractions;
using ModelContextProtocol.Protocol;

namespace LoopRuntime.Tests;

public sealed class RemoteMcpToolsTests
{
    [Fact]
    public async Task LoadFileAsync_UsesConfiguredAgentIdentityIdForTokenAcquisition()
    {
        const string agentIdentityId = "6e1590ce-042e-49e8-bfd1-75104fa813a7";
        var authProvider = new RecordingAuthorizationHeaderProvider();
        var tools = new RemoteMcpTools(
            ConfigurationFor("Mcp:Scopes:0", "api://mcp/access_as_user", agentIdentityId),
            HttpContextAccessor(),
            authProvider,
            NullLogger<RemoteMcpTools>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => tools.LoadFileAsync("/tmp/example.py", CancellationToken.None));

        Assert.Equal(agentIdentityId, authProvider.LastOptions?.AcquireTokenOptions.ExtraParameters?["fmiPathForClientAssertion"]);
    }

    [Fact]
    public async Task A2ACheckerClient_UsesConfiguredAgentIdentityIdForTokenAcquisition()
    {
        const string agentIdentityId = "6e1590ce-042e-49e8-bfd1-75104fa813a7";
        var authProvider = new RecordingAuthorizationHeaderProvider();
        var checker = new A2ACheckerClient(
            ConfigurationFor("Checker:Scopes:0", "api://checker/access_as_user", agentIdentityId),
            HttpContextAccessor(),
            authProvider,
            NullLogger<A2ACheckerClient>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => checker.ReviewAsync("session", "/tmp/example.py", CancellationToken.None));

        Assert.Equal(agentIdentityId, authProvider.LastOptions?.AcquireTokenOptions.ExtraParameters?["fmiPathForClientAssertion"]);
    }

    [Fact]
    public void ExtractTextContent_ReturnsTextFromTextContentBlock()
    {
        var result = new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = "ok" }
            }
        };

        var text = RemoteMcpTools.ExtractTextContent(result);

        Assert.Equal("ok", text);
    }

    private static IConfiguration ConfigurationFor(string scopeKey, string scope, string agentIdentityId)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:AgentGateway:Gateway:0"] = "http://127.0.0.1:1",
                ["Mcp:PathSuffix"] = "/mcp-from-exec",
                [scopeKey] = scope,
                ["AgentIdentity:AgentIdentityId"] = agentIdentityId
            })
            .Build();
    }

    private static IHttpContextAccessor HttpContextAccessor()
    {
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user")], "test"))
            }
        };
    }

    private sealed class RecordingAuthorizationHeaderProvider : IAuthorizationHeaderProvider
    {
        public AuthorizationHeaderProviderOptions? LastOptions { get; private set; }

        public Task<string> CreateAuthorizationHeaderAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? options = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult("Bearer fake-token");
        }

        public Task<string> CreateAuthorizationHeaderForAppAsync(
            string scopes,
            AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = downstreamApiOptions;
            return Task.FromResult("Bearer fake-token");
        }

        public Task<string> CreateAuthorizationHeaderForUserAsync(
            IEnumerable<string> scopes,
            AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
            ClaimsPrincipal? claimsPrincipal = null,
            CancellationToken cancellationToken = default)
        {
            LastOptions = authorizationHeaderProviderOptions;
            return Task.FromResult("Bearer fake-token");
        }
    }
}
