using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LoopRuntime.Contracts;

namespace LoopRuntime.Tests;

public sealed class ExecutorAuthTests : IClassFixture<WebApplicationFactory<LoopRuntime.Executor.Program>>
{
    private readonly WebApplicationFactory<LoopRuntime.Executor.Program> _factory;

    public ExecutorAuthTests(WebApplicationFactory<LoopRuntime.Executor.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "768437b2-e373-41b5-9748-875e1507d85b",
                    ["AzureAd:ClientId"] = "ba511c70-c2c8-4374-94d3-26742a4ede58",
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                    ["Services:AgentGateway:Gateway:0"] = "http://localhost:3032",
                    ["Mcp:PathSuffix"] = "/mcp-from-exec",
                    ["AI:Provider"] = "fake",
                    ["AI:Model"] = "fake"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<Microsoft.Extensions.AI.IChatClient, LoopRuntime.Agents.Fakes.FakeChatClient>();

                services
                    .AddAuthentication(TestAuthScheme.Name)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthScheme.Name, _ => { });

                services.AddAuthorization(options =>
                {
                    options.AddPolicy(TestAuthScheme.Name, policy =>
                    {
                        policy.AddAuthenticationSchemes(TestAuthScheme.Name);
                        policy.RequireAuthenticatedUser();
                    });
                });
            });
        });
    }

    [Fact]
    public async Task Unauthenticated_Run_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/run", new { prompt = "test" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Run_CanReadOidClaim()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthScheme.Name, "token");

        var response = await client.PostAsJsonAsync("/run", new { prompt = "test" });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static class TestAuthScheme
    {
        public const string Name = "Test";
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.Authorization.ToString().StartsWith(TestAuthScheme.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.Fail("Missing test token"));
            }

            var claims = new[] { new Claim("oid", "test-oid") };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
