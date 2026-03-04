# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```sh
# Build (also downloads Copilot CLI binary ~56MB on first run)
dotnet build DotNetClaw.csproj

# Run (requires secrets set first — see below)
dotnet run --project DotNetClaw.csproj

# Expose locally via tunnel (separate terminal)
ngrok http 5000
```

## Secrets (required before running)

```sh
dotnet user-secrets init
dotnet user-secrets set "Twilio:AccountSid" "ACxxx"
dotnet user-secrets set "Twilio:AuthToken"  "xxx"
dotnet user-secrets set "Slack:BotToken"    "xoxb-xxx"
dotnet user-secrets set "Slack:SigningSecret" "xxx"
dotnet user-secrets set "Slack:BotUserId"   "UXXXXX"
```

Also required: `gh auth login` + `gh extension install github/gh-copilot` for the Copilot CLI backend.

## Architecture

Single ASP.NET Core app, two inbound channels, one agent.

```
WhatsApp user          Slack user
    ↓ POST /whatsapp/webhook    ↓ POST /slack/events
WhatsAppChannel.cs     SlackChannel.cs
    └──────────┬────────────────┘
               ↓  sessionId + message text
          ClawRuntime.cs
          ConcurrentDictionary<sessionId, AgentSession>
               ↓
          AIAgent  (Microsoft.Agents.AI RC1)
          ReAct loop + streaming + tool calling
               ↓
          CopilotClient  (GitHub.Copilot.SDK 0.1.30)
          spawns copilot-cli child process (stdio JSON-RPC)
               ↓
          mind/  directory
          SOUL.md + .github/agents/assistant.agent.md
```

**Session IDs** determine conversation isolation:
- WhatsApp: `"whatsapp:+1234567890"` (raw `From` field)
- Slack DM: `"slack:dm:{userId}"`
- Slack @mention: `"slack:{channelId}:{userId}"`

## The Mind Concept

The `mind/` directory is the agent's persistent identity (from [msclaw](https://github.com/ianphil/msclaw)):

- `mind/SOUL.md` — personality and mission, always loaded as the system message prefix
- `mind/.github/agents/*.agent.md` — behavioral instructions; YAML frontmatter is stripped before use; Copilot CLI also discovers these natively via `Cwd = mind.MindRoot`
- `mind/.working-memory/memory.md` — facts the agent accumulates (created at runtime)

`MindLoader.LoadSystemMessageAsync()` joins SOUL.md + agent files with `\n\n---\n\n` separator. This becomes the `instructions` parameter passed to `copilotClient.AsAIAgent(...)`.

## Key API Gotchas (verified from DLL inspection)

These diverge from documentation or intuition:

| What you might expect | Actual API |
|---|---|
| `CopilotClient.AsChatClient()` | Does not exist. Use `copilotClient.AsAIAgent(ownsClient, id, name, description, tools, instructions)` directly. Extension is in `GitHub.Copilot.SDK` namespace, defined in the bridge package. |
| `IAIAgent` interface | Does not exist. The type is `AIAgent` (abstract class). |
| `AgentResponseUpdate.Content` | Property is `.Text`. |
| `RunStreamingAsync(message, session, ct)` | Takes 4 params: `(string, AgentSession, AgentRunOptions?, CancellationToken)`. |
| `IEventHandler<T>.Handle(T e, CancellationToken ct)` | SlackNet's interface is `Task Handle(T e)` — no `CancellationToken`. |
| `AppMentionEvent` in SlackNet | Type is `AppMention` in `SlackNet.Events`. |
| `SlackNet.Message` | Is `SlackNet.WebApi.Message` — needs `using SlackNet.WebApi;`. |
| `[ValidateTwilioRequest]` attribute | Removed in Twilio.AspNet.Core v8.x. Use `.AddEndpointFilter<ValidateTwilioRequestFilter>()` on the route. |
| `UseSlackNet(c => c.UseSigningSecret(...))` | Signing secret must go in `AddSlackNet(c => c.UseSigningSecret(...))` — putting it in `UseSlackNet` is obsolete. |
| `UseSlackNet` on `IEndpointRouteBuilder` | Requires `IApplicationBuilder`. Call on `WebApplication` directly. |

## Adding Tools

To give the agent new capabilities, add `AITool` instances to the `tools` parameter in `Program.cs`:

```csharp
var tools = new List<AITool>
{
    AIFunctionFactory.Create(myService.SomeMethodAsync),
};
return copilotClient.AsAIAgent(ownsClient: true, ..., tools: tools, instructions: systemMessage);
```

## Adding Channels

Each channel follows the same pattern: receive a message, derive a `sessionId`, call `runtime.HandleAsync(sessionId, text, ct)`, send the reply back. See `WhatsAppChannel.cs` for the minimal template.

## Config Reference

Non-secret values live in `appsettings.json`. Secrets via `dotnet user-secrets`.

| Key | Where |
|---|---|
| `Mind:Path` | appsettings (`"./mind"`) |
| `Copilot:Model` | appsettings (`"claude-sonnet-4-5"`) — note: model selection requires `SessionConfig` overload of `AsAIAgent`; current code uses instruction overload |
| `Twilio:AccountSid`, `Twilio:AuthToken` | user-secrets |
| `Twilio:From` | appsettings (sandbox number) |
| `Slack:BotToken`, `Slack:SigningSecret`, `Slack:BotUserId` | user-secrets |
| `Slack:Policy` | appsettings (`"open"` or `"allowlist"`) |
| `Slack:AllowedUserIds` | appsettings (comma-separated, only used when policy = `"allowlist"`) |
