using System.Net;
using System.Security.Claims;
using AgenticTodo.TodoApi.Adapters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Abstractions;
using Xunit;

namespace AgenticTodo.Tests.TodoApi;

public class PassthroughAgentGatewayTests
{
    [Fact]
    public async Task ForwardAsync_forwards_the_inbound_authorization_header_verbatim()
    {
        var capturingHandler = new CapturingHttpMessageHandler();
        var gateway = new PassthroughAgentGateway(new FakeHttpClientFactory(capturingHandler));

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer inbound-token";

        using var response = await gateway.ForwardAsync(HttpMethod.Post, "/agent/todos", null, httpContext, CancellationToken.None);

        Assert.NotNull(capturingHandler.LastRequest);
        Assert.Equal("Bearer inbound-token", capturingHandler.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ForwardAsync_sends_no_authorization_header_when_none_is_present()
    {
        var capturingHandler = new CapturingHttpMessageHandler();
        var gateway = new PassthroughAgentGateway(new FakeHttpClientFactory(capturingHandler));

        var httpContext = new DefaultHttpContext();

        using var response = await gateway.ForwardAsync(HttpMethod.Get, "/agent/todos", null, httpContext, CancellationToken.None);

        Assert.Null(capturingHandler.LastRequest!.Headers.Authorization);
    }
}

public class OboAgentGatewayTests
{
    [Fact]
    public async Task ForwardAsync_requests_the_todoagent_scope_and_forwards_the_exchanged_header()
    {
        var capturingHandler = new CapturingHttpMessageHandler();
        var headerProvider = new FakeAuthorizationHeaderProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent:ClientId"] = "todoagent-client-id" })
            .Build();
        var gateway = new OboAgentGateway(new FakeHttpClientFactory(capturingHandler), headerProvider, configuration);

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        using var response = await gateway.ForwardAsync(HttpMethod.Post, "/agent/todos", null, httpContext, CancellationToken.None);

        Assert.Equal(["api://todoagent-client-id/access_as_user"], headerProvider.RequestedScopes);
        Assert.Equal("Bearer exchanged-token", capturingHandler.LastRequest!.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task ForwardAsync_throws_when_agent_client_id_is_missing()
    {
        var gateway = new OboAgentGateway(
            new FakeHttpClientFactory(new CapturingHttpMessageHandler()),
            new FakeAuthorizationHeaderProvider(),
            new ConfigurationBuilder().Build());

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity()) };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gateway.ForwardAsync(HttpMethod.Post, "/agent/todos", null, httpContext, CancellationToken.None));
    }
}

file sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://gateway.local") };
}

file sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}

file sealed class FakeAuthorizationHeaderProvider : IAuthorizationHeaderProvider
{
    public List<string>? RequestedScopes { get; private set; }

    public Task<string> CreateAuthorizationHeaderAsync(
        IEnumerable<string> scopes,
        AuthorizationHeaderProviderOptions? options = null,
        ClaimsPrincipal? claimsPrincipal = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Bearer exchanged-token");

    public Task<string> CreateAuthorizationHeaderForAppAsync(
        string scopes,
        AuthorizationHeaderProviderOptions? downstreamApiOptions = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult("Bearer exchanged-token");

    public Task<string> CreateAuthorizationHeaderForUserAsync(
        IEnumerable<string> scopes,
        AuthorizationHeaderProviderOptions? authorizationHeaderProviderOptions = null,
        ClaimsPrincipal? claimsPrincipal = null,
        CancellationToken cancellationToken = default)
    {
        RequestedScopes = scopes.ToList();
        return Task.FromResult("Bearer exchanged-token");
    }
}
