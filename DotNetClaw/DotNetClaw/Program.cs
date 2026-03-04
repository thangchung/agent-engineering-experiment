using DotNetClaw;
using GitHub.Copilot.SDK;
using Microsoft.Agents.AI;
using Scalar.AspNetCore;
using Twilio;
using Twilio.AspNet.Core;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// 1. MIND — agent identity (SOUL.md + .github/agents/ + .working-memory/)
// ============================================================================
// Mirrors msclaw's IdentityLoader. Assembles SOUL.md + *.agent.md files
// into the system message. Copilot CLI also reads .github/agents/ natively
// (via Cwd = mind root) — they reinforce each other.

builder.Services.AddSingleton<MindLoader>();

// ============================================================================
// 2. GITHUB COPILOT SDK + MAF BRIDGE → AIAgent
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
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();

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
        description:  "Personal AI assistant on WhatsApp and Slack",
        tools:        null,
        instructions: systemMessage);
});

builder.Services.AddSingleton<ClawRuntime>();

// ============================================================================
// 3. WHATSAPP CHANNEL (Twilio)
// ============================================================================
// Set secrets: dotnet user-secrets set "Twilio:AccountSid" "ACxxx"
//              dotnet user-secrets set "Twilio:AuthToken"  "xxx"

var accountSid = builder.Configuration["Twilio:AccountSid"]
    ?? throw new InvalidOperationException("Twilio:AccountSid not configured. Run: dotnet user-secrets set \"Twilio:AccountSid\" \"ACxxx\"");
var authToken = builder.Configuration["Twilio:AuthToken"]
    ?? throw new InvalidOperationException("Twilio:AuthToken not configured.");

TwilioClient.Init(accountSid, authToken);
builder.Services
    .AddTwilioClient()
    .AddTwilioRequestValidation();

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

// WhatsApp webhook — Twilio posts form-encoded messages here
app.MapWhatsApp();

// Slack events endpoint — UseSlackNet registers middleware + /slack/events route
// Must be called on WebApplication (IApplicationBuilder), not IEndpointRouteBuilder
app.MapSlack(builder.Configuration);

app.Run();
