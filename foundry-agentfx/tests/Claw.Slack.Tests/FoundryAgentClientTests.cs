using System.Net;
using System.Net.Http.Json;
using Claw.Slack;
using Microsoft.Extensions.Configuration;

namespace Claw.Slack.Tests;

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
}

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        handler(request);
}
