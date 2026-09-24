using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace CoffeeShop.Tests;

/// <summary>
/// tasks.md T02: ServiceDefaults wires OpenTelemetry sources and (research.md B11)
/// does NOT add a resilience handler to every HttpClient by default.
/// </summary>
public class ServiceDefaultsTests
{
    [Fact]
    public void AddServiceDefaults_ConfiguresTracing_ForCoffeeShopCounterSource()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();

        using var app = builder.Build();

        // Resolving TracerProvider triggers OTel's internal ActivityListener registration for
        // every source passed to AddSource(...) in ConfigureOpenTelemetry (Extensions.cs).
        app.Services.GetRequiredService<TracerProvider>();

        using var source = new ActivitySource("CoffeeShop.Counter");
        using var activity = source.StartActivity("probe");

        // .NET only allocates an Activity when something is listening for this source name.
        // A non-null activity here proves the TracerProvider subscribed to "CoffeeShop.Counter".
        Assert.NotNull(activity);
    }

    [Fact]
    public void AddServiceDefaults_ConfiguresTracing_ForAgentAndWorkflowSources()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();

        using var app = builder.Build();
        app.Services.GetRequiredService<TracerProvider>();

        using var agentSource = new ActivitySource("Experimental.Microsoft.Agents.AI");
        using var agentActivity = agentSource.StartActivity("probe");
        Assert.NotNull(agentActivity);

        using var mcpSource = new ActivitySource("Experimental.ModelContextProtocol");
        using var mcpActivity = mcpSource.StartActivity("probe");
        Assert.NotNull(mcpActivity);

        using var appAgentsSource = new ActivitySource("CoffeeShop.Agents");
        using var appAgentsActivity = appAgentsSource.StartActivity("probe");
        Assert.NotNull(appAgentsActivity);

        // "Microsoft.Agents.AI.Workflows*" is a wildcard AddSource entry - a concrete child
        // source name under that prefix must also be picked up.
        using var workflowSource = new ActivitySource("Microsoft.Agents.AI.Workflows");
        using var workflowActivity = workflowSource.StartActivity("probe");
        Assert.NotNull(workflowActivity);
    }

    [Fact]
    public async Task ConfigureHttpClientDefaults_DoesNotAddResilienceHandler_ToPlainClients()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();

        var attempts = 0;
        builder.Services.AddHttpClient("plain")
            .ConfigurePrimaryHttpMessageHandler(() => new CountingFailingHandler(() => attempts++));

        using var app = builder.Build();
        var factory = app.Services.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("plain");

        // The standard resilience handler retries a transient 503 a few times by default.
        // With no resilience handler registered, exactly one attempt must be made.
        using var response = await client.GetAsync("http://plain.invalid/");

        Assert.Equal(1, attempts);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class CountingFailingHandler(Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        }
    }
}
