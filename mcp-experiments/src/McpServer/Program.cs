using System.Reflection;
using System.Text.Json;
using McpServer.Cli;
using McpServer.Cli.Commands;
using McpServer.Cli.Infrastructure;
using McpServer;
using McpServer.Bootstrap;
using McpServer.CodeMode;
using McpServer.OpenApi;
using McpServer.Registry;
using McpServer.Search;
using McpServer.Services;
using McpServer.ToolSearch;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;

// Streamable-HTTP MCP server. Connect via MCP Inspector or any HTTP-based MCP client:
//   Transport: Streamable HTTP
//   URL:       http://localhost:5100/mcp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
CliConfig cliConfig = ConfigLoader.Load(builder.Configuration);
bool cliVerbose = args.Contains("--verbose", StringComparer.OrdinalIgnoreCase);

// MCP clients sometimes probe non-exposed tool names; suppress noisy internal error logs for those expected probes.
builder.Logging.AddFilter("ModelContextProtocol.Server.McpServer", LogLevel.Critical);

if (cliConfig.EnableCliMode && !cliVerbose)
{
    builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
    builder.Logging.AddFilter("Polly", LogLevel.Warning);
}

builder.AddServiceDefaults();

// OpenAPI-generated handlers use the default HTTP client and resolve the base URL
// from OpenAPI servers or configuration override.
builder.Services.AddHttpClient();

List<OpenApiToolCatalogBuilder.OpenApiSourceDefinition> openApiSources =
[
    .. OpenApiToolCatalogBuilder.ResolveSources(builder.Configuration, AppContext.BaseDirectory),
];

IReadOnlyList<string> codeModeBaseUrls;
List<ToolDescriptor> tools =
[
    new ToolDescriptor(
        Name: "status",
        Description: "Returns MCP server health and timestamp.",
        InputJsonSchema: """{"type":"object","properties":{}}""",
        Tags: ["system"],
        IsPinned: true,
        IsSynthetic: false,
        IsVisible: _ => true,
        Handler: (_, _) => Task.FromResult<object?>(new { ok = true, timestamp = DateTimeOffset.UtcNow })),
];

// BuildAsync loads each OpenAPI document exactly once and returns both tools and base URLs.
(IReadOnlyList<ToolDescriptor> openApiTools, codeModeBaseUrls) = await OpenApiToolCatalogBuilder.BuildAsync(openApiSources);
tools.AddRange(openApiTools);

if (cliConfig.EnableStatistic)
{
    BootstrapConsoleReporter.WriteReport(openApiSources, tools);
}

builder.Services.AddSingleton<IToolRegistry>(new ToolRegistry(tools));
builder.Services.AddSingleton<IToolSearcher, WeightedToolSearcher>();
builder.Services.AddSingleton<MetaTools>();
builder.Services.AddSingleton<ToolInvoker>();
builder.Services.AddSingleton<DiscoveryTools>();
// Runtime selection defaults to local constrained execution.
// Set CodeMode:Runner=opensandbox and OpenSandbox:* settings to enable OpenSandbox-backed preflight.
builder.Services.AddSingleton<ISandboxRunner>(sp =>
    SandboxRunnerFactory.Create(
        builder.Configuration,
        sp.GetRequiredService<ILoggerFactory>(),
        codeModeBaseUrls));
builder.Services.AddSingleton<ExecuteTool>();
builder.Services.AddSingleton<CopilotChatService>();
// Default anonymous context — replace with per-request auth resolution in production.
builder.Services.AddSingleton(new UserContext());
builder.Services.AddSingleton(new CliRuntimeOptions(cliVerbose));

if (cliConfig.EnableCliMode)
{
    await RunCliModeAsync(builder.Services, args);
    return;
}

// WithHttpTransport uses the MCP streamable-HTTP protocol (not legacy SSE).
// WithToolsFromAssembly discovers all [McpServerToolType] classes in this assembly.
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

WebApplication app = builder.Build();

HashSet<string> exposedMcpToolNames = McpHandlerCatalog.GetExposedToolNames();

app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/mcp", StringComparison.OrdinalIgnoreCase))
    {
        context.Request.EnableBuffering();
        try
        {
            using JsonDocument document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
            if (document.RootElement.TryGetProperty("method", out JsonElement methodElement) &&
                string.Equals(methodElement.GetString(), "tools/call", StringComparison.Ordinal) &&
                document.RootElement.TryGetProperty("params", out JsonElement paramsElement) &&
                paramsElement.ValueKind == JsonValueKind.Object &&
                paramsElement.TryGetProperty("name", out JsonElement nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                string? toolName = nameElement.GetString();
                if (!string.IsNullOrWhiteSpace(toolName) && !exposedMcpToolNames.Contains(toolName))
                {
                    app.Logger.LogInformation(
                        "MCP unknown tool probe: {ToolName}. This is expected for non-exposed tools in tool-Search mode.",
                        toolName);
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON frames are ignored here; MCP endpoint handles protocol parsing.
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Position = 0;
            }
        }
    }

    await next();
});

app.MapDefaultEndpoints();

// Expose the root IServiceProvider so tool handlers can resolve services
// needed by generated tool handlers. Handlers are registered before Build() so they cannot
// receive the provider via constructor injection; this static accessor bridges that gap.
ToolServiceProvider.Root = app.Services;

// /mcp is the default endpoint URL the MCP Inspector UI expects.
app.MapMcp("/mcp");

// Minimal API endpoints for the TestWeb chat UI.
app.MapPost("/chat/send", async (ChatPromptRequest request, CopilotChatService chatService, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
    {
        return Results.BadRequest(new { error = "Prompt cannot be empty." });
    }

    try
    {
        CopilotChatService.ChatTurnMetrics metrics = await chatService.SendPromptAsync(request.Prompt, ct);
        return Results.Ok(metrics);
    }
    catch (TimeoutException)
    {
        return Results.Problem(
            title: "Copilot request timed out",
            detail: "The Copilot backend did not return a response in time. Please retry with a shorter prompt or increase Copilot:SendTimeoutSeconds.",
            statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        return Results.Problem(
            title: "Request canceled",
            detail: "The request was canceled before completion.",
            statusCode: StatusCodes.Status408RequestTimeout);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Problem(
            title: "Copilot session failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/chat/reset", async (CopilotChatService chatService, CancellationToken ct) =>
{
    await chatService.ResetAsync(ct);
    return Results.NoContent();
});

await app.RunAsync();

static async Task RunCliModeAsync(IServiceCollection services, string[] args)
{
    IServiceCollection cliServices = new ServiceCollection();
    foreach (ServiceDescriptor descriptor in services)
    {
        cliServices.Add(descriptor);
    }

    TypeRegistrar registrar = new(cliServices);
    CommandApp app = new(registrar);

    app.Configure(config =>
    {
        config.SetApplicationName("mcp-server-cli");
        config.ValidateExamples();
        config.PropagateExceptions();
        config.AddBranch("tools", tools =>
        {
            tools.AddCommand<ToolsListCommand>("list");
            tools.AddCommand<ToolsShowCommand>("show");
        });
        config.AddCommand<QueryCommand>("query");
    });

    ToolServiceProvider.Root = registrar.GetServiceProvider();

    string[] effectiveArgs = args.Length == 0 ? ["--help"] : args;
    await app.RunAsync(effectiveArgs);
}
