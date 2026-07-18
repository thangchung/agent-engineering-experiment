using LoopRuntime.Agents;
using LoopRuntime.Contracts;
using LoopRuntime.Sandbox;
using LoopRuntime.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;

namespace LoopRuntime.Executor;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration)
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
        builder.Services.AddAgentIdentities();
        UseScopedOidcFicSignedAssertionProvider(builder.Services);
        builder.Services.AddAuthorization();

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("LoopRuntime.Executor"));

        // Sandbox: DockerSandbox for P1/P2; swap to real sandbox via config later
        builder.Services.AddSingleton<ICodeSandbox>(_ => new DockerSandbox());
        builder.Services.AddScoped<IMcpTools, RemoteMcpTools>();
        builder.Services.AddScoped<IChecker, A2ACheckerClient>();
        builder.Services.AddHttpContextAccessor();

        // AI:Provider = "fake" (deterministic) or "gateway" (AgentGateway).
        // Keys via dotnet user-secrets (dev) or env vars (CI). Never in code.
        builder.Services.AddChatClient(builder.Configuration);

        builder.Services.AddScoped<ExecutorAgent>();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapDefaultEndpoints();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapPost("/run", [Authorize] async (RunRequest request, ExecutorAgent executor, HttpContext httpContext, ILogger<Program> logger, CancellationToken cancellationToken) =>
        {
            var prompt = string.IsNullOrWhiteSpace(request.Prompt)
                ? "Write a Fibonacci program that prints the first 20 numbers"
                : request.Prompt;

            var userId = httpContext.User.FindFirst("oid")?.Value;
            logger.LogInformation("Run request received. PromptLength={PromptLength} UserId={UserId}", prompt.Length, userId);
            var result = await executor.RunAsync(prompt, maxIterations: 5, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Run request completed. SessionId={SessionId} Iterations={Iterations} Completed={Completed}", result.SessionId, result.Iterations, result.Completed);

            return Results.Ok(new
            {
                sessionId = result.SessionId,
                path = result.Path,
                iterations = result.Iterations,
                completed = result.Completed,
                finalResponse = result.FinalResponse,
                finalCode = result.FinalCode,
                status = result.Completed ? "ok" : "needs-work"
            });
        });

        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "LoopRuntime.Executor" })).AllowAnonymous();
        app.MapGet("/", (HttpContext context) =>
        {
            context.Response.Redirect("/index.html");
            return Task.CompletedTask;
        }).AllowAnonymous();

        app.Run();
    }

    private static void UseScopedOidcFicSignedAssertionProvider(IServiceCollection services)
    {
        ReplaceServiceLifetime(services, typeof(ICredentialsLoader), "Microsoft.Identity.Web.DefaultCertificateLoader", ServiceLifetime.Scoped);
        ReplaceServiceLifetime(services, typeof(ICustomSignedAssertionProvider), "Microsoft.Identity.Web.OidcFic.OidcIdpSignedAssertionLoader", ServiceLifetime.Scoped);
    }

    private static void ReplaceServiceLifetime(IServiceCollection services, Type serviceType, string implementationTypeName, ServiceLifetime lifetime)
    {
        var descriptor = services.FirstOrDefault(service =>
            service.ServiceType == serviceType &&
            service.ImplementationType?.FullName == implementationTypeName);
        if (descriptor?.ImplementationType is null)
        {
            return;
        }

        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(serviceType, descriptor.ImplementationType, lifetime));
    }
}

internal sealed record RunRequest(string Prompt);
