using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    /// <summary>
    /// Ollama agent OpenTelemetry source name.
    /// </summary>
    public const string OllamaOtelSource = "AgentService.Ollama";
    
    /// <summary>
    /// Foundry Local agent OpenTelemetry source name.
    /// </summary>
    public const string FoundryLocalOtelSource = "AgentService.FoundryLocal";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // enable AI telemetry
        AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // GenAI Semantic Conventions metrics (from OpenTelemetryChatClient)
                    // - gen_ai.client.operation.duration (histogram, seconds)
                    // - gen_ai.client.token.usage (histogram, tokens)
                    .AddMeter(OllamaOtelSource)
                    .AddMeter(FoundryLocalOtelSource)
                    .AddMeter("Microsoft.Extensions.AI");  // Default MEAI metrics
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource("OpenAI.ChatClient")
                    .AddSource("Experimental.ModelContextProtocol")
                    // GenAI Semantic Conventions traces (from OpenTelemetryChatClient)
                    // - Span: "chat {model}" with gen_ai.* attributes
                    // - Span: "execute_tool {tool.name}" for tool calls
                    .AddSource(OllamaOtelSource)
                    .AddSource(FoundryLocalOtelSource)
                    .AddSource("Microsoft.Extensions.AI")  // Default MEAI traces
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }
}
