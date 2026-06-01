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
// UseAIContextProviders extension is in Microsoft.Extensions.AI namespace via Microsoft.Agents.AI

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
    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
    var startupLog = loggerFactory.CreateLogger("Claw.Api.Startup");

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

        // Memory tools — agent infrastructure, write directly to mind/.working-memory/
        // NOT routed through gateway: need local filesystem access + always-on (no discovery needed)
        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("A durable fact to remember, e.g. 'User's usual order is oat latte'")] string fact,
                CancellationToken ct = default) =>
            {
                var path = Path.Combine(mind.MindRoot, ".working-memory", "memory.md");
                await File.AppendAllTextAsync(path, $"\n- {fact}", ct);
                return "Fact saved.";
            },
            "SaveFact",
            "Save a durable fact to persistent memory. Call immediately when user shares a preference, name, setting, or any detail worth remembering across sessions."),

        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("A behavioral rule to remember, e.g. 'Never re-ask for email if already provided'")] string rule,
                CancellationToken ct = default) =>
            {
                var path = Path.Combine(mind.MindRoot, ".working-memory", "rules.md");
                await File.AppendAllTextAsync(path, $"\n- {rule}", ct);
                return "Rule saved.";
            },
            "AddRule",
            "Save a behavioral correction or preference as a persistent rule. Call when user corrects your behavior or states how they want you to respond."),

        AIFunctionFactory.Create(
            async (
                [System.ComponentModel.Description("Session log entry, e.g. 'Session: user ordered 2 oat lattes, order confirmed, ID=abc'")] string entry,
                CancellationToken ct = default) =>
            {
                var path = Path.Combine(mind.MindRoot, ".working-memory", "log.md");
                await File.AppendAllTextAsync(path, $"\n- [{DateTime.UtcNow:yyyy-MM-dd HH:mm}] {entry}", ct);
                return "Log entry appended.";
            },
            "AppendLog",
            "Append a session observation to the persistent log. Call at session start, on task completion, and before ending — write what was done, pending items, next steps."),
    };

    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool registered: {Name}", tool.Name);

    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    var functionTools = tools.OfType<AIFunction>().ToList();

    // Skills provider: loads SKILL.md files from mind/skills/ at startup (file-based, no scripts needed)
    var skillsDir = Path.Combine(AppContext.BaseDirectory, "mind", "skills");
#pragma warning disable MAAI001
    var skillsProvider = new AgentSkillsProvider(skillsDir, null, null, null, loggerFactory);
#pragma warning restore MAAI001
    startupLog.LogInformation("[Agent] Skills directory: {Path}", skillsDir);

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
            tools: tools,
            clientFactory: chatClient => chatClient.AsBuilder()
                .UseAIContextProviders(skillsProvider)
                .Build());
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
    var logger = sp.GetRequiredService<ILogger<RetryOrderingAgent>>();
    var innerAgent = new FoundryOrderingAgent(agent);
    return new RetryOrderingAgent(innerAgent, logger);
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

file sealed class RetryOrderingAgent(IOrderingAgent inner, ILogger logger) : IOrderingAgent
{
    public string Name => inner.Name;
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default) => inner.CreateSessionAsync(ct);

    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        string message, AgentSession session, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        const int maxAttempts = 5;
        List<AgentResponseUpdate> buffer = [];

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            buffer.Clear();

            try
            {
                await foreach (var update in inner.RunStreamingAsync(message, session, ct))
                    buffer.Add(update);
                break; // success
            }
            catch (Exception ex) when (IsTooManyRequests(ex) && attempt < maxAttempts)
            {
                var delayMs = (int)Math.Pow(2, attempt) * 1000; // 2s, 4s, 8s, 16s
                logger.LogWarning(ex, "[Retry] 429 Too Many Requests — attempt {Attempt}/{Max}, delay {Delay}ms", attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs, ct);
            }
        }

        // yield outside try-catch
        foreach (var update in buffer)
            yield return update;
    }

    private static bool IsTooManyRequests(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException hre && hre.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return true;
            if (current.Message.Contains("429", StringComparison.Ordinal)
                || current.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("TooManyRequests", StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

file sealed class FoundryOrderingAgent(AIAgent inner) : IOrderingAgent
{
    public string Name => "OrderingAgent";
    public ValueTask<AgentSession> CreateSessionAsync(CancellationToken ct = default) => inner.CreateSessionAsync(ct);
    public IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(string message, AgentSession session, CancellationToken ct = default) => inner.RunStreamingAsync(message, session, null, ct);
}
