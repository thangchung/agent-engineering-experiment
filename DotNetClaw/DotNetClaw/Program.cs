using Azure.AI.Projects;
using Azure.Identity;
using DotNetClaw;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(ClawTelemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(ClawTelemetry.MeterName));

builder.Services.AddSingleton<MindLoader>();

builder.Services.AddSingleton<MemoryTool>(sp =>
{
    var mind   = sp.GetRequiredService<MindLoader>();
    var mlogger = sp.GetRequiredService<ILogger<MemoryTool>>();
    return new MemoryTool(mind.MindRoot, mlogger);
});

builder.Services.AddSingleton<ExecTool>(sp =>
{
    var elogger = sp.GetRequiredService<ILogger<ExecTool>>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new ExecTool(elogger, config);
});
builder.Services.AddSingleton<SkillLoaderTool>();

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind          = sp.GetRequiredService<MindLoader>();
    var memTool       = sp.GetRequiredService<MemoryTool>();
    var execTool      = sp.GetRequiredService<ExecTool>();
    var skillLoader   = sp.GetRequiredService<SkillLoaderTool>();
    var config        = sp.GetRequiredService<IConfiguration>();
    var startupLog    = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DotNetClaw.Startup");
    var ollamaEnabled = config["Ollama:Enabled"]?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    var ollamaBaseUrl = config["Ollama:BaseUrl"] ?? "http://localhost:11434/v1";
    var ollamaModel = config["Ollama:Model"]   ?? "qwen3-coder:480b-cloud";
    
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();

    var skillsJson = skillLoader.ListSkillsAsync().GetAwaiter().GetResult();
    var mcpMode = string.Equals(config["CoffeeshopCli:Mode"], "mcp", StringComparison.OrdinalIgnoreCase);
    var mcpAvailable = mcpMode ? !HasTopLevelError(skillsJson) : false;

    if (mcpMode)
    {
        if (mcpAvailable)
            startupLog.LogInformation("[Skills] MCP mode active and reachable; ExecTool is disabled.");
        else
            startupLog.LogWarning("[Skills] MCP mode active but unavailable; enabling ExecTool fallback.");
    }

    string skillAdvertisement = "";
    try
    {
        var skillNames = ExtractSkillNames(skillsJson);

        startupLog.LogInformation("[Skills] Discovered {Count} skills: {Names}", 
            skillNames.Count, string.Join(", ", skillNames));

        if (skillNames.Any())
        {
            skillAdvertisement = $"\n\n---\n\n## Available Skills\n\n" +
                $"You have access to {skillNames.Count} agent skill(s) that define multi-step workflows:\n\n" +
                string.Join("\n", skillNames.Select(n => $"- `{n}`")) + "\n\n" +
                "**To use a skill:** Call `load_skill('<skill-name>')` to get step-by-step instructions. " +
                "Then follow the workflow defined in the skill manifest.";
        }
    }
    catch (Exception ex)
    {
        startupLog.LogWarning(ex, "[Skills] Failed to discover skills at startup (non-critical)");
    }

    var tools = new List<AITool>
    {
        AIFunctionFactory.Create(memTool.AppendLogAsync),
        AIFunctionFactory.Create(memTool.AddRuleAsync),
        AIFunctionFactory.Create(memTool.SaveFactAsync),
        AIFunctionFactory.Create(skillLoader.ListSkillsAsync),
        AIFunctionFactory.Create(skillLoader.LoadSkillAsync),
    };

    if (mcpMode && mcpAvailable)
    {
        tools.Add(AIFunctionFactory.Create(skillLoader.ListMenuItemsAsync));
        tools.Add(AIFunctionFactory.Create(skillLoader.LookupCustomerAsync));
        tools.Add(AIFunctionFactory.Create(skillLoader.SubmitOrderAsync));
    }

    if (!mcpMode || !mcpAvailable)
    {
        tools.Add(AIFunctionFactory.Create(execTool.RunAsync));
    }

    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool registered: {Name} — {Description}",
            tool.Name,
            (tool.Description ?? "").Length > 80 ? tool.Description![..80] + "…" : tool.Description ?? "");
    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    var functionTools = tools.OfType<AIFunction>().ToList();
    var instructions  = systemMessage + skillAdvertisement;
    var provider      = config["Agent:Provider"] ?? "copilot";

    startupLog.LogInformation("[Agent] Provider: {Provider}", provider);

    // ── Foundry provider ────────────────────────────────────────
    if (string.Equals(provider, "foundry", StringComparison.OrdinalIgnoreCase))
    {
        var endpoint = config["Foundry:Endpoint"]
            ?? throw new InvalidOperationException("Foundry:Endpoint is required when Agent:Provider is 'foundry'.");
        var model = config["Foundry:Model"] ?? "gpt-5.4-mini";

        if (ollamaEnabled)
            startupLog.LogWarning("[Agent] Ollama settings are ignored when using the Foundry provider.");

        startupLog.LogInformation("[Agent] Foundry mode: model={Model} endpoint={Endpoint}", model, endpoint);

        var aiProjectClient = new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
        return aiProjectClient.AsAIAgent(
            model:        model,
            instructions: instructions,
            name:         "DotNetClaw",
            tools:        tools);
    }

    // ── Copilot provider (default) ──────────────────────────────
    var gitHubToken = config["Copilot:GitHubToken"];
    var hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
    var isLocal = hostEnvironment.IsDevelopment();
    var useExplicitToken = !isLocal && !string.IsNullOrWhiteSpace(gitHubToken);

    if (useExplicitToken && gitHubToken!.StartsWith("ghp_", StringComparison.Ordinal))
        throw new InvalidOperationException("Classic PATs (ghp_) are not supported. Use a fine-grained PAT (github_pat_), OAuth (gho_), or user token (ghu_).");

    var copilotOptions = new CopilotClientOptions
    {
        Cwd       = mind.MindRoot,
        AutoStart = true,
        UseStdio  = true,
    };

    if (useExplicitToken)
    {
        copilotOptions.GitHubToken = gitHubToken;
        copilotOptions.UseLoggedInUser = false;
    }

    startupLog.LogInformation("[Auth] local={Local}, tokenConfigured={Configured}, explicitToken={Explicit}",
        isLocal, !string.IsNullOrWhiteSpace(gitHubToken), useExplicitToken);

    var copilotClient = new CopilotClient(copilotOptions);

    if (ollamaEnabled)
        startupLog.LogInformation("[Agent] Ollama mode: model={Model} baseUrl={BaseUrl}", ollamaModel, ollamaBaseUrl);

    var sessionConfig = new SessionConfig
    {
        SystemMessage = new SystemMessageConfig
        {
            Mode    = SystemMessageMode.Replace,
            Content = instructions,
        },
        Tools = functionTools,
        OnPermissionRequest = PermissionHandler.ApproveAll,
    };

    if (ollamaEnabled)
    {
        sessionConfig.Provider = new ProviderConfig { Type = "openai", BaseUrl = ollamaBaseUrl };
        sessionConfig.Model    = ollamaModel;
    }

    return copilotClient.AsAIAgent(
        sessionConfig: sessionConfig,
        ownsClient:    true,
        id:            "dotnetclaw",
        name:          "DotNetClaw",
        description:   "Personal AI assistant");
});

builder.Services.AddSingleton<ClawRuntime>();

builder.Services.AddSlackChannel(builder.Configuration);

builder.Services.AddOpenApi();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("DotNetClaw API"));
}

app.MapWebChannel();

app.MapSlack(builder.Configuration);

app.Run();

static bool HasTopLevelError(string payload)
{
    if (string.IsNullOrWhiteSpace(payload))
        return true;

    try
    {
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
               doc.RootElement.TryGetProperty("error", out _);
    }
    catch
    {
        return true;
    }
}

static List<string> ExtractSkillNames(string payload)
{
    var skillNames = new List<string>();

    if (string.IsNullOrWhiteSpace(payload))
        return skillNames;

    using var skillDoc = System.Text.Json.JsonDocument.Parse(payload);
    var root = skillDoc.RootElement;

    if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        AddSkillNamesFromArray(root, skillNames);
        return skillNames;
    }

    if (root.ValueKind == System.Text.Json.JsonValueKind.Object &&
        root.TryGetProperty("skills", out var skillsElement) &&
        skillsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
        AddSkillNamesFromArray(skillsElement, skillNames);
    }

    return skillNames;
}

static void AddSkillNamesFromArray(System.Text.Json.JsonElement skillsArray, List<string> skillNames)
{
    foreach (var item in skillsArray.EnumerateArray())
    {
        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
        if (!item.TryGetProperty("name", out var nameProp)) continue;

        var name = nameProp.GetString();
        if (!string.IsNullOrEmpty(name))
            skillNames.Add(name);
    }
}
