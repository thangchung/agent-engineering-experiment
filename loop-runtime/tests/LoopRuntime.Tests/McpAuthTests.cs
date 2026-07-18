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

public sealed class McpAuthTests : IClassFixture<WebApplicationFactory<LoopRuntime.Mcp.Program>>
{
    private readonly WebApplicationFactory<LoopRuntime.Mcp.Program> _factory;

    public McpAuthTests(WebApplicationFactory<LoopRuntime.Mcp.Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:TenantId"] = "768437b2-e373-41b5-9748-875e1507d85b",
                    ["AzureAd:ClientId"] = "b228aac6-5596-4cd6-bf8e-8bc343613268",
                    ["AzureAd:Instance"] = "https://login.microsoftonline.com/"
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
    public async Task Unauthenticated_Mcp_Post_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_Mcp_Post_PassesAuthorizationGate()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthScheme.Name, "token");

        var response = await client.PostAsJsonAsync("/", new { });

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
