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
        // Regex avoids strict JSON parse — coffeeshop-cli output may contain literal newlines
        var skillNames = new List<string>();
        var regex = new System.Text.RegularExpressions.Regex(@"""name"":\s*""([^""]+)""");
        var matches = regex.Matches(skillsJson);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                var name = match.Groups[1].Value;
                if (!string.IsNullOrEmpty(name))
                    skillNames.Add(name);
            }
        }
        
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
    {
        startupLog.LogInformation("[Agent] Ollama mode: model={Model} baseUrl={BaseUrl}", ollamaModel, ollamaBaseUrl);
        var ollamaSessionConfig = new SessionConfig
        {
            Provider = new ProviderConfig { Type = "openai", BaseUrl = ollamaBaseUrl },
            Model    = ollamaModel,
            SystemMessage = new SystemMessageConfig
            {
                Mode    = SystemMessageMode.Replace,
                Content = systemMessage + skillAdvertisement,
            },
            Tools = functionTools,
            // approve all tool calls — MAF handles tool security at the channel layer
            OnPermissionRequest = PermissionHandler.ApproveAll,
        };
        return copilotClient.AsAIAgent(
            sessionConfig: ollamaSessionConfig,
            ownsClient:    true,
            id:            "dotnetclaw",
            name:          "DotNetClaw",
            description:   "Personal AI assistant");
    }

    var defaultSessionConfig = new SessionConfig
    {
        SystemMessage = new SystemMessageConfig
        {
            Mode    = SystemMessageMode.Replace,
            Content = systemMessage + skillAdvertisement,
        },
        Tools = functionTools,
        OnPermissionRequest = PermissionHandler.ApproveAll,
    };

    return copilotClient.AsAIAgent(
        sessionConfig: defaultSessionConfig,
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
