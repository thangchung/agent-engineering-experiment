using A2A;
using A2A.AspNetCore;
using LoopRuntime.Agents;
using LoopRuntime.Checker;
using LoopRuntime.Contracts;
using LoopRuntime.Sandbox;
using LoopRuntime.ServiceDefaults;
using Microsoft.Agents.AI.Hosting.A2A;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.AI;
using Microsoft.Identity.Abstractions;
using Microsoft.Identity.Web;

namespace LoopRuntime.Checker;

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
            .WithTracing(tracing => tracing.AddSource("LoopRuntime.Checker"));

        builder.Services.AddSingleton<ICodeSandbox>(_ => new DockerSandbox());
        builder.Services.AddScoped<IMcpTools, RemoteMcpTools>();

        // AI:Provider = "fake" (deterministic) or "gateway" (AgentGateway).
        // Keys via dotnet user-secrets (dev) or env vars (CI). Never in code.
        builder.Services.AddChatClient(builder.Configuration);

        builder.Services.AddScoped<IChecker, CheckerAgent>();
        builder.Services.AddKeyedSingleton<Microsoft.Agents.AI.AIAgent, CheckerAIAgent>("checker");

        builder.AddA2AServer("checker");

        var app = builder.Build();

        app.Logger.LogInformation("Checker service starting. Endpoint={Url}", app.Urls.FirstOrDefault() ?? "configured by Aspire");

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapDefaultEndpoints();

        // A2A JSON-RPC endpoint + agent card. AgentGateway A2A policy expects these at root.
        var gatewayUrl = app.Configuration["Services:AgentGateway:Gateway:0"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("AgentGateway URL is not configured. Ensure the Checker project references the 'agentgateway' resource in AppHost.");

        var agentCard = new AgentCard
        {
            Name = "checker",
            Description = "Reviews Python code and returns a verdict.",
            Version = "1.0.0",
            Capabilities = new AgentCapabilities { Streaming = true },
            Skills = [new AgentSkill { Id = "review", Name = "Code Review", Description = "Reviews Python code and returns a verdict." }],
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            SupportedInterfaces =
            [
                new AgentInterface { ProtocolBinding = ProtocolBindingNames.JsonRpc, Url = gatewayUrl }
            ]
        };
        app.MapA2AJsonRpc("checker", "/").RequireAuthorization();
        app.MapWellKnownAgentCard(agentCard, string.Empty).RequireAuthorization();

        // Plain HTTP A2A endpoint kept for direct diagnostics/tests
        app.MapCheckerEndpoints().RequireAuthorization();

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
