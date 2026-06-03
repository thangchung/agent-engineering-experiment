using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using Claw.Channels;
using Microsoft.Extensions.Configuration;

namespace Claw.Channels.Tests;

public sealed class FoundryAgentClientTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task InvokeAsync_SendsCorrectPayload()
    {
        HttpRequestMessage? captured = null;
        string? capturedBody = null;

        var handler = new FakeHttpMessageHandler(async req =>
        {
            captured = req;
            capturedBody = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output":"latte confirmed"}""", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var config = BuildConfig(new() { ["Agent:InvocationsPath"] = "/invocations" });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var client = new FoundryAgentClient(http, config);

        var result = await client.InvokeAsync("order a latte", "slack:C1:U1");

        Assert.Equal("latte confirmed", result);
        Assert.NotNull(captured);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.Contains("agent_session_id=slack%3AC1%3AU1", captured.RequestUri!.Query);
        Assert.Contains("order a latte", capturedBody);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsRawJson_WhenNoOutputProperty()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", System.Text.Encoding.UTF8, "application/json")
            }));

        var config = BuildConfig(new() { ["Agent:InvocationsPath"] = "/invocations" });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var client = new FoundryAgentClient(http, config);

        var result = await client.InvokeAsync("hello", "s1");

        Assert.Equal("""{"status":"ok"}""", result);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsPlainText_WhenResponseIsNotJson()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Hello from hosted agent", System.Text.Encoding.UTF8, "text/plain")
            }));

        var config = BuildConfig(new() { ["Agent:InvocationsPath"] = "/invocations" });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var client = new FoundryAgentClient(http, config);

        var result = await client.InvokeAsync("hello", "s1");

        Assert.Equal("Hello from hosted agent", result);
    }

    [Fact]
    public async Task InvokeAsync_ThrowsOnNonSuccess()
    {
        var handler = new FakeHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var config = BuildConfig(new() { ["Agent:InvocationsPath"] = "/invocations" });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var client = new FoundryAgentClient(http, config);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.InvokeAsync("hello", "s1"));
    }

    [Fact]
    public async Task InvokeAsync_UsesDefaultPath_WhenNotConfigured()
    {
        HttpRequestMessage? captured = null;
        var handler = new FakeHttpMessageHandler(async req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output":"ok"}""", System.Text.Encoding.UTF8, "application/json")
            };
        });

        var config = BuildConfig(new());
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
        var client = new FoundryAgentClient(http, config);

        await client.InvokeAsync("hi", "s1");

        Assert.NotNull(captured);
        Assert.StartsWith("/invocations", captured!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task InvokeAsync_CreatesFoundrySession_WhenTokenResourceConfigured()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (requests.Count == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("""{"agent_session_id":"foundry-session-1"}""", System.Text.Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"output":"menu"}""", System.Text.Encoding.UTF8, "application/json")
            });
        });

        var config = BuildConfig(new()
        {
            ["Agent:InvocationsPath"] = "agents/claw-agent/endpoint/protocols/invocations?api-version=v1",
            ["Agent:TokenResource"] = "https://ai.azure.com"
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/api/projects/project/") };
        var client = new FoundryAgentClient(http, config, new FakeTokenCredential());

        var result = await client.InvokeAsync("menu", "slack:C1:U1");

        Assert.Equal("menu", result);
        Assert.Equal(2, requests.Count);
        Assert.Equal("/api/projects/project/agents/claw-agent/endpoint/sessions?api-version=v1", requests[0].RequestUri!.PathAndQuery);
        Assert.Equal("/api/projects/project/agents/claw-agent/endpoint/protocols/invocations?api-version=v1&agent_session_id=foundry-session-1", requests[1].RequestUri!.PathAndQuery);
        Assert.True(requests.All(r => r.Headers.Contains("Foundry-Features")));
    }
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request);
}

internal sealed class FakeTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        ValueTask.FromResult(GetToken(requestContext, cancellationToken));
}
