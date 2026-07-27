using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AgenticTodo.ServiceDefaults;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public const string SuperAdminPolicy = "RequireSuperAdmin";

    // Fails closed if the `groups` claim is absent -- which happens whenever the token's
    // audience app is missing groupMembershipClaims=SecurityGroup, even for a real SuperAdminGroup member.
    public static TBuilder AddGroupAuthorization<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var superAdminGroupId = builder.Configuration["Authorization:SuperAdminGroupId"]
            ?? throw new InvalidOperationException("Authorization:SuperAdminGroupId is required.");

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(SuperAdminPolicy, policy => policy.RequireClaim("groups", superAdminGroupId));

        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, FriendlyForbiddenResponseHandler>();

        return builder;
    }

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

        AppContext.SetSwitch("OpenAI.Experimental.EnableOpenTelemetry", true);

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath) &&
                            !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("*");
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
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapHealthChecks(HealthEndpointPath);
            app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
            {
                Predicate = r => r.Tags.Contains("live")
            });
        }

        return app;
    }

    // Each gateway hop replaces the Authorization header before forwarding, so the inbound
    // token seen here already IS the post-exchange token for that hop. Development check is
    // hard-coded inside this method (not left to callers) so it can't be enabled in Staging/Production.
    private static readonly Lock TokenDiagnosticsLock = new();
    private static TokenDiagnosticsSnapshot? lastTokenSnapshot;

    public static WebApplication UseTokenDiagnostics(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        app.Use(async (context, next) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authorization))
            {
                var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authorization["Bearer ".Length..]
                    : authorization;
                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AgenticTodo.Diagnostics.TokenLogging");
                logger.LogInformation("Inbound JWT on {Method} {Path}: {Token}", context.Request.Method, context.Request.Path, token);

                var snapshot = TryDecodeSnapshot(context.Request.Method, context.Request.Path, token);
                if (snapshot is not null)
                {
                    lock (TokenDiagnosticsLock)
                    {
                        lastTokenSnapshot = snapshot;
                    }
                }
            }

            await next();
        });

        app.MapGet("/diagnostics/last-token", () =>
        {
            lock (TokenDiagnosticsLock)
            {
                return lastTokenSnapshot is null ? Results.NotFound() : Results.Ok(lastTokenSnapshot);
            }
        });

        return app;
    }

    private static TokenDiagnosticsSnapshot? TryDecodeSnapshot(string method, string path, string token)
    {
        var segments = token.Split('.');
        if (segments.Length < 2)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64UrlDecode(segments[1]));
            var root = document.RootElement;
            return new TokenDiagnosticsSnapshot(
                method,
                path,
                root.TryGetProperty("aud", out var aud) ? aud.GetString() : null,
                root.TryGetProperty("scp", out var scp) ? scp.GetString() : null,
                root.TryGetProperty("sub", out var sub) ? sub.GetString() : null,
                root.TryGetProperty("oid", out var oid) ? oid.GetString() : null);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}

public sealed record TokenDiagnosticsSnapshot(string Method, string Path, string? Aud, string? Scp, string? Sub, string? Oid);

// Message is deliberately generic -- no admin email or other PII in a response any caller can trigger.
public sealed class FriendlyForbiddenResponseHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly AuthorizationMiddlewareResultHandler DefaultHandler = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await DefaultHandler.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
            title = "Forbidden",
            status = StatusCodes.Status403Forbidden,
            detail = "You do not have permission to perform this action. Contact application admin to request access.",
        });
    }
}
