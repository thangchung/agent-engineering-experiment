# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```sh
# Build (also downloads Copilot CLI binary ~56MB on first run)
dotnet build DotNetClaw.csproj

# Run (requires secrets set first — see below)
dotnet run --project DotNetClaw.csproj

# Run with Aspire 13 (requires secrets set first — see below)
dotnet run --project DotNetClaw.AppHost.csproj
```

## Secrets (required before running)

```sh
dotnet user-secrets init

# Slack (required)
dotnet user-secrets set "Slack:BotToken"    "xoxb-xxx"
dotnet user-secrets set "Slack:SigningSecret" "xxx"
dotnet user-secrets set "Slack:BotUserId"   "UXXXXX"

# Teams (optional — from Azure Bot registration)
dotnet user-secrets set "Teams:MicrosoftAppId"       "xxx"
dotnet user-secrets set "Teams:MicrosoftAppPassword" "xxx"
dotnet user-secrets set "Teams:MicrosoftAppTenantId" "xxx"

# Telegram (optional — from @BotFather)
dotnet user-secrets set "Telegram:BotToken" "xxx"

# Discord (optional — from Discord Developer Portal)
dotnet user-secrets set "Discord:BotToken" "xxx"
```

Also required: `gh auth login` + `gh extension install github/gh-copilot` for the Copilot CLI backend.

## Architecture

Single ASP.NET Core app, four inbound channels, one agent.

```
Teams user        Slack user        Telegram user     Discord user
    ↓ POST            ↓ Socket Mode     ↓ Polling         ↓ WebSocket GW
    /api/messages     (SlackNet)        (BackgroundSvc)   (BackgroundSvc)
TeamsChannel.cs   SlackChannel.cs   TelegramChannel.cs  DiscordChannel.cs
    └──────────────────┬──────────────────┘──────────────────┘
                       ↓  sessionId + message text
                  ClawRuntime.cs
                  ConcurrentDictionary<sessionId, AgentSession>
                  /reset · /status · /help command interception
                       ↓
                  AIAgent  (Microsoft.Agents.AI RC1)
                  ReAct loop + streaming + tool calling
                       ↓
                  CopilotClient  (GitHub.Copilot.SDK)
                  spawns copilot-cli child process (stdio JSON-RPC)
                       ↓
                  mind/  directory
                  SOUL.md + .github/agents/assistant.agent.md
                  + .working-memory/  (memory.md · rules.md · log.md)
```

**Session IDs** determine conversation isolation:
- Teams: `"teams:{conversationId}"`
- Slack DM: `"slack:dm:{userId}"`
- Slack @mention: `"slack:{channelId}:{userId}"`
- Telegram: `"telegram:{chatId}"`
- Discord DM: `"discord:dm:{userId}"`
- Discord channel: `"discord:{channelId}:{userId}"`

## Folder Structure

Layered boundaries — dependencies point inward:

```
┌──────────────────────────────────────────────────────────────────┐
│                         Channels                                 │
│  TeamsChannel.cs · SlackChannel.cs · TelegramChannel.cs          │  ← Inbound transports
│  DiscordChannel.cs                                               │    (webhook / socket / polling)
├──────────────────────────────────────────────────────────────────┤
│                         Runtime                                  │
│  ClawRuntime.cs                                                  │  ← Session management,
│                                                                  │    chat command interception
├──────────────────────────────────────────────────────────────────┤
│                          Agent                                   │
│  AIAgent (MAF) · ExecTool.cs · MemoryTool.cs · McpToolLoader.cs  │  ← ReAct loop, tools, MCP
├──────────────────────────────────────────────────────────────────┤
│                           Mind                                   │
│  MindLoader.cs · mind/SOUL.md · mind/.github/agents/             │  ← Identity, instructions,
│  mind/.working-memory/  (memory.md · rules.md · log.md)          │    persistent memory
└──────────────────────────────────────────────────────────────────┘
```

Rules:
- **Channels** call only `ClawRuntime.HandleAsync()` — never the agent directly
- **ClawRuntime** owns session lifecycle and command interception — no platform logic
- **Agent layer** is pure tool orchestration — no channel or session awareness
- **Mind** is files on disk — no C# code; `MindLoader` is the only bridge

File layout:

```
DotNetClaw/
├── Program.cs                        ← Composition root, DI wiring, all registrations
├── ClawRuntime.cs                    ← Session store, /reset·/status·/help dispatch
├── MindLoader.cs                     ← Assembles system message from mind/ files
│
├── SlackChannel.cs                   ← SlackNet Socket Mode (DM + @mention)
├── TeamsChannel.cs                   ← Bot Framework webhook  [planned]
├── TelegramChannel.cs                ← Long-polling BackgroundService  [planned]
├── DiscordChannel.cs                 ← WebSocket BackgroundService  [planned]
│
├── MemoryTool.cs                     ← AppendLog · AddRule · SaveFact  [planned]
├── ExecTool.cs                       ← Shell execution + safety filter  [planned]
├── McpToolLoader.cs                  ← Loads MCP servers from config  [planned]
│
├── appsettings.json                  ← Non-secret config (Mind:Path, Mcp:Servers, …)
│
└── mind/                             ← Agent identity (edit here to change behaviour)
    ├── SOUL.md                       ← Personality, mission, boundaries
    ├── .github/agents/
    │   └── assistant.agent.md        ← Operational instructions + memory protocol
    └── .working-memory/
        ├── memory.md                 ← Curated facts (read every session)
        ├── rules.md                  ← One-liner lessons from mistakes
        └── log.md                    ← Raw session log (last 50 lines injected)

DotNetClaw.AppHost/
└── Program.cs                        ← Aspire orchestration (local dev only)
```

## The Mind Concept

The `mind/` directory is the agent's persistent identity (from [msclaw](https://github.com/ianphil/msclaw)):

- `mind/SOUL.md` — personality and mission, always loaded as the system message prefix
- `mind/.github/agents/*.agent.md` — behavioral instructions; YAML frontmatter is stripped before use; Copilot CLI also discovers these natively via `Cwd = mind.MindRoot`
- `mind/.working-memory/memory.md` — curated facts (read every session, rarely written)
- `mind/.working-memory/rules.md` — one-liner lessons from mistakes (append-only)
- `mind/.working-memory/log.md` — raw session observations (last 50 lines injected)

`MindLoader.LoadSystemMessageAsync()` joins SOUL.md + agent files + working-memory files with `\n\n---\n\n` separator. This becomes the `instructions` parameter passed to `copilotClient.AsAIAgent(...)`.

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
| `UseSlackNet(c => c.UseSigningSecret(...))` | Signing secret must go in `AddSlackNet(c => c.UseSigningSecret(...))` — putting it in `UseSlackNet` is obsolete. |
| `UseSlackNet` on `IEndpointRouteBuilder` | Requires `IApplicationBuilder`. Call on `WebApplication` directly. |
| Teams `IBotFrameworkHttpAdapter` | Use `CloudAdapter` from `Microsoft.Bot.Builder.Integration.AspNet.Core`. Register both adapter and `IBot` (your `ActivityHandler` subclass). |
| Discord `GatewayIntents` | Must include `GatewayIntents.MessageContent` or message text will be empty. |
| Telegram `GetUpdates` | Use `timeout: 30` for long-polling. Increment `offset` by `update.Id + 1` to avoid reprocessing. |

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

Each channel follows the same pattern: receive a message, derive a `sessionId`, call `runtime.HandleAsync(sessionId, text, ct)`, send the reply back. See `SlackChannel.cs` for the full reference implementation.

Webhook channels (Teams) register via `AddXxxChannel()` + `MapXxxChannel()`. Polling/WebSocket channels (Telegram, Discord) register as `BackgroundService` — no endpoint mapping needed. Channels auto-disable when their token config is empty.

## Config Reference

Non-secret values live in `appsettings.json`. Secrets via `dotnet user-secrets`.

| Key | Where |
|---|---|
| `Mind:Path` | appsettings (`"./mind"`) |
| `Copilot:Model` | appsettings (`"claude-sonnet-4-5"`) — note: model selection requires `SessionConfig` overload of `AsAIAgent`; current code uses instruction overload |
| `Slack:BotToken`, `Slack:SigningSecret`, `Slack:BotUserId` | user-secrets |
| `Slack:Policy` | appsettings (`"open"` or `"allowlist"`) |
| `Slack:AllowedUserIds` | appsettings (comma-separated, only used when policy = `"allowlist"`) |
| `Teams:MicrosoftAppId`, `Teams:MicrosoftAppPassword` | user-secrets |
| `Teams:MicrosoftAppTenantId` | user-secrets (optional, single-tenant only) |
| `Telegram:BotToken` | user-secrets (channel disabled if empty) |
| `Discord:BotToken` | user-secrets (channel disabled if empty) |
| `CoffeeshopCli:ExecutablePath` | appsettings (path to coffeeshop-cli executable) |
| `CoffeeshopCli:Port` | appsettings (default: `5001`, port for coffeeshop-cli server to avoid conflicts) |
| `Mcp:Servers` | appsettings (array of `{ Name, Transport, Command, Args }` or `{ Name, Transport, Url }`) |
