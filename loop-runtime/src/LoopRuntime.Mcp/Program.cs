using LoopRuntime.Agents;
using LoopRuntime.Contracts;
using LoopRuntime.Sandbox;
using LoopRuntime.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using ModelContextProtocol.Server;

namespace LoopRuntime.Mcp;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration);
        builder.Services.AddAuthorization();

        builder.Services.AddSingleton<ICodeSandbox>(_ => new DockerSandbox());
        builder.Services.AddSingleton<IMcpTools, McpLocalTools>();

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource("LoopRuntime.Mcp"));

        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapDefaultEndpoints();

        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "LoopRuntime.Mcp" })).AllowAnonymous();
        app.MapMcp().RequireAuthorization();

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("MCP server starting. Endpoint={Url}", app.Urls.FirstOrDefault() ?? "configured by Aspire");

        await app.RunAsync();
    }
}
