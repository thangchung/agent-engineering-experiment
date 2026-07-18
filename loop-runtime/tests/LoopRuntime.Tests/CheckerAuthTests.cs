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

namespace LoopRuntime.Tests;

public sealed class CheckerAuthTests : IClassFixture<WebApplicationFactory<LoopRuntime.Checker.Program>>
{
    private readonly WebApplicationFactory<LoopRuntime.Checker.Program> _factory;

    public CheckerAuthTests(WebApplicationFactory<LoopRuntime.Checker.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "768437b2-e373-41b5-9748-875e1507d85b",
                    ["AzureAd:ClientId"] = "40d39e9a-69f7-4bf2-b50e-c126c07fd2bd",
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                    ["Services:AgentGateway:Gateway:0"] = "http://localhost:3032"
                });
            });

            builder.ConfigureTestServices(services =>
            {
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
    public async Task Unauthenticated_A2A_Post_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/", new { jsonrpc = "2.0", method = "tasks/send", id = 1 });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_AgentCard_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/.well-known/agent-card.json");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_A2A_Post_PassesAuthorizationGate()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthScheme.Name, "token");

        var response = await client.PostAsJsonAsync("/", new { jsonrpc = "2.0", method = "tasks/send", id = 1 });

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_AgentCard_PassesAuthorizationGate()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthScheme.Name, "token");

        var response = await client.GetAsync("/.well-known/agent-card.json");

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

            var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
