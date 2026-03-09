using DotNetClaw;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 1. MIND — agent identity (SOUL.md + .github/agents/ + .working-memory/)
// ============================================================================
// Mirrors msclaw's IdentityLoader. Assembles SOUL.md + *.agent.md files
// into the system message. Copilot CLI also reads .github/agents/ natively
// (via Cwd = mind root) — they reinforce each other.

builder.Services.AddSingleton<MindLoader>();

// ============================================================================
// 2. MEMORY TOOLS — give the agent write-access to its working-memory files
// ============================================================================
builder.Services.AddSingleton<MemoryTool>(sp =>
{
    var mind   = sp.GetRequiredService<MindLoader>();
    var mlogger = sp.GetRequiredService<ILogger<MemoryTool>>();
    return new MemoryTool(mind.MindRoot, mlogger);
});

// ============================================================================
// 2b. EXECUTION + SKILL TOOLS — shell execution and skill loading
// ============================================================================
builder.Services.AddSingleton<ExecTool>(sp =>
{
    var elogger = sp.GetRequiredService<ILogger<ExecTool>>();
    var config = sp.GetRequiredService<IConfiguration>();
    return new ExecTool(elogger, config);
});
builder.Services.AddSingleton<SkillLoaderTool>();

// ============================================================================
// 3. GITHUB COPILOT SDK + MAF BRIDGE → AIAgent
// ============================================================================
// Microsoft.Agents.AI.GitHub.Copilot provides CopilotClient.AsAIAgent()
// extension (in GitHub.Copilot.SDK namespace). Goes CopilotClient → AIAgent
// directly — no IChatClient intermediate needed.
//
// Two useful overloads:
//   AsAIAgent(SessionConfig?, ownsClient, id, name, description)         — sets model
//   AsAIAgent(ownsClient, id, name, description, tools, instructions)    — sets system prompt
//
// We use overload 2 to pass SOUL.md + agent files as the system message.
// The Copilot CLI also reads .github/agents/ natively via Cwd (msclaw pattern).

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind          = sp.GetRequiredService<MindLoader>();
    var memTool       = sp.GetRequiredService<MemoryTool>();
    var execTool      = sp.GetRequiredService<ExecTool>();
    var skillLoader   = sp.GetRequiredService<SkillLoaderTool>();
    var startupLog    = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DotNetClaw.Startup");
    
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();

    // Discover available skills at startup and inject into system message
    var skillsJson = skillLoader.ListSkillsAsync().GetAwaiter().GetResult();
    string skillAdvertisement = "";
    try
    {
        // Extract skill names using regex pattern matching instead of strict JSON parsing
        // This works around the issue of literal newlines in coffeeshop-cli JSON output
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
        AIFunctionFactory.Create(execTool.RunAsync),
        AIFunctionFactory.Create(skillLoader.ListSkillsAsync),
        AIFunctionFactory.Create(skillLoader.LoadSkillAsync),
    };

    // Log each registered tool so we can verify names at startup
    foreach (var tool in tools.OfType<AIFunction>())
        startupLog.LogInformation("[Agent] Tool registered: {Name} — {Description}",
            tool.Name,
            (tool.Description ?? "").Length > 80 ? tool.Description![..80] + "…" : tool.Description ?? "");
    startupLog.LogInformation("[Agent] Total tools: {Count}", tools.Count);

    var copilotClient = new CopilotClient(new CopilotClientOptions
    {
        Cwd       = mind.MindRoot,  // CLI reads .github/agents/ natively (msclaw pattern)
        AutoStart = true,
        UseStdio  = true,
    });

    // AsAIAgent(ownsClient, id, name, description, tools, instructions)
    return copilotClient.AsAIAgent(
        ownsClient:   true,
        id:           "dotnetclaw",
        name:         "DotNetClaw",
        description:  "Personal AI assistant",
        tools:        tools,
        instructions: systemMessage + skillAdvertisement);
});

builder.Services.AddSingleton<ClawRuntime>();

// ============================================================================
// 4. SLACK CHANNEL (SlackNet Socket Mode)
// ============================================================================
// Set secrets: dotnet user-secrets set "Slack:BotToken" "xoxb-..."
//              dotnet user-secrets set "Slack:AppToken"  "xapp-..."   ← app-level token, connections:write scope
//              dotnet user-secrets set "Slack:BotUserId" "UXXXXX"

builder.Services.AddSlackChannel(builder.Configuration);

// ============================================================================
// 5. OPENAPI

// ============================================================================
builder.Services.AddOpenApi();

// ============================================================================
// 6. BUILD + ENDPOINTS
// ============================================================================

var app = builder.Build();

// OpenAPI + Scalar UI (dev only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("DotNetClaw API"));
}

// Health check
app.MapGet("/", () => Results.Ok(new { name = "DotNetClaw", status = "running" }));

// Slack events endpoint — UseSlackNet registers middleware + /slack/events route
// Must be called on WebApplication (IApplicationBuilder), not IEndpointRouteBuilder
app.MapSlack(builder.Configuration);

app.Run();
