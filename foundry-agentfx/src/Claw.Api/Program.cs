using Azure.AI.AgentServer.Invocations;
using Azure.AI.Projects;
using Azure.Identity;
using Claw.Api;
using Claw.Api.Agents;
using Claw.Core;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Foundry platform injects PORT env var; respect it so /readiness is reachable
// v16: force rebuild with correct ToolSearch Gateway URL
var foundryPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(foundryPort) && int.TryParse(foundryPort, out var portNum))
{
    builder.WebHost.UseUrls($"http://*:{portNum}");
}

var isHostedMode = string.Equals(
    builder.Configuration["Agent:HostedMode"], "foundry",
    StringComparison.OrdinalIgnoreCase);

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(ClawTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(ClawTelemetry.MeterName));

builder.Services.AddHttpClient<IToolSearchClient, ToolSearchClient>(client =>
{
    var gatewayUrl = builder.Configuration["Services:ToolSearchGateway:Url"]
        ?? "http://localhost:5002";
    client.BaseAddress = new Uri(gatewayUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddSingleton<MindLoader>();

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var toolSearch = sp.GetRequiredService<IToolSearchClient>();
    var mind = sp.GetRequiredService<MindLoader>();
    var config = sp.GetRequiredService<IConfiguration>();
    var startupLog = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Claw.Api.Startup");

    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    var provider = config["Agent:Provider"] ?? "copilot";

    startupLog.LogInformation("[Agent] Provider: {Provider}", provider);

    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Natural language query to find tools, e.g. 'web search', 'menu', 'order'")] string query,
                int limit = 10,
                CancellationToken ct = default) =>
                await toolSearch.SearchToolsAsync(query, limit <= 0 ? 10 : limit, ct),
            "search_tools",
            "Search the tool catalog by natural language query. Returns tool names, descriptions, and inputJsonSchema. Call this before call_tool to discover available tools."),

        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Exact tool name from search_tools results, e.g. 'web_search', 'menu_list_items', 'order_submit'")] string name,
                [System.ComponentModel.Description("Tool arguments as a JSON object matching the tool's inputJsonSchema. For web_search use {\"query\":\"your search terms\"}. For menu tools use {} or {\"id\":\"...\"}.")] System.Text.Json.JsonElement arguments,
                CancellationToken ct) =>
                await toolSearch.CallToolAsync(name, arguments, ct),
            "call_tool",
            "Invoke a discovered tool by name with its arguments. ALWAYS call search_tools first to find the tool name and schema, then call this."),
    };

    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool registered: {Name}", tool.Name);

    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    var functionTools = tools.OfType<AIFunction>().ToList();

    if (string.Equals(provider, "foundry", StringComparison.OrdinalIgnoreCase))
    {
        // Platform injects FOUNDRY_PROJECT_ENDPOINT (single _); .NET config reads Foundry__Endpoint (double __)
        var endpoint = config["Foundry:Endpoint"]
            ?? Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
            ?? throw new InvalidOperationException("Foundry:Endpoint required when Agent:Provider is 'foundry'.");
        var model = config["Foundry:Model"]
            ?? Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
            ?? "gpt-4o-mini";

        startupLog.LogInformation("[Agent] Foundry mode: model={Model} endpoint={Endpoint}", model, endpoint);

        var aiProjectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
        var foundryAgent = aiProjectClient.AsAIAgent(
            model: model,
            instructions: systemMessage,
            name: "ClawAgent",
            tools: tools);
        var otelAgent = new OpenTelemetryAgent(foundryAgent, ClawTelemetry.ActivitySourceName);
        otelAgent.EnableSensitiveData = sp.GetRequiredService<IHostEnvironment>().IsDevelopment();
        return otelAgent;
    }

    var gitHubToken = config["Copilot:GitHubToken"];
    var hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
    var isLocal = hostEnvironment.IsDevelopment();
    var useExplicitToken = !isLocal && !string.IsNullOrWhiteSpace(gitHubToken);

    if (useExplicitToken && gitHubToken!.StartsWith("ghp_", StringComparison.Ordinal))
        throw new InvalidOperationException("Classic PATs (ghp_) are not supported. Use a fine-grained PAT.");

    var copilotOptions = new CopilotClientOptions
    {
        Cwd = mind.MindRoot,
        AutoStart = true,
        UseStdio = true,
    };

    if (useExplicitToken)
    {
        copilotOptions.GitHubToken = gitHubToken;
        copilotOptions.UseLoggedInUser = false;
    }

    startupLog.LogInformation("[Auth] local={Local}, explicitToken={Explicit}", isLocal, useExplicitToken);

    var copilotClient = new CopilotClient(copilotOptions);

    var sessionConfig = new SessionConfig
    {
        SystemMessage = new SystemMessageConfig
        {
            Mode = SystemMessageMode.Replace,
            Content = systemMessage,
        },
        Tools = functionTools,
        OnPermissionRequest = PermissionHandler.ApproveAll,
    };

    return copilotClient.AsAIAgent(
        sessionConfig: sessionConfig,
        ownsClient: true,
        id: "claw-agent",
        name: "ClawAgent",
        description: "Coffeeshop AI assistant with tool search");
});

builder.Services.AddSingleton<ClawRuntime>();

builder.Services.AddSingleton<IOrderingAgent>(sp =>
{
    var agent = sp.GetRequiredService<AIAgent>();
    return new FoundryOrderingAgent(agent);
});

builder.Services.AddSingleton<CoffeeshopWorkflow>();

// Foundry Hosted Agent — invocations protocol (gated: only when Agent:HostedMode=foundry)
if (isHostedMode)
{
    builder.Services.AddInvocationsServer();
    builder.Services.AddScoped<CoffeeshopInvocationHandler>();
    builder.Services.AddScoped<InvocationHandler>(sp => sp.GetRequiredService<CoffeeshopInvocationHandler>());
}

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

// Override /readiness with simplest possible endpoint for Foundry
app.MapGet("/readiness", () => Results.Ok());

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("Claw API"));
}

app.MapWebChannel();

// Map /invocations endpoint when in hosted mode (DI registered above)
if (isHostedMode)
{
    app.MapPost("/", async (
        HttpRequest request,
        HttpResponse response,
        CoffeeshopInvocationHandler handler,
        CancellationToken cancellationToken) =>
    {
        var invocationId = request.Headers.TryGetValue("x-agent-invocation-id", out var invocationHeader)
            && !string.IsNullOrWhiteSpace(invocationHeader)
            ? invocationHeader.ToString()
            : Guid.NewGuid().ToString("N");

        var sessionId = request.Headers.TryGetValue("x-agent-session-id", out var sessionHeader)
            && !string.IsNullOrWhiteSpace(sessionHeader)
            ? sessionHeader.ToString()
            : request.Query.TryGetValue("agent_session_id", out var querySessionId)
                && !string.IsNullOrWhiteSpace(querySessionId)
                ? querySessionId.ToString()
                : Environment.GetEnvironmentVariable("FOUNDRY_AGENT_SESSION_ID") ?? $"invocation:{invocationId}";

        response.Headers["x-agent-invocation-id"] = invocationId;
        response.Headers["x-agent-session-id"] = sessionId;
        await handler.HandleDirectAsync(request, response, invocationId, sessionId, cancellationToken);
    });

    app.MapInvocationsServer();
}

app.Run();

file sealed class FoundryOrderingAgent(AIAgent inner) : IOrderingAgent
{
    public string Name => "OrderingAgent";
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default) => inner.CreateSessionAsync(ct);
    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(string message, AgentSession session, CancellationToken ct = default) => inner.RunStreamingAsync(message, session, null, ct);
}
