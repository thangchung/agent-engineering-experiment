# Copilot Instructions

## Principles

- KISS / YAGNI / DRY / Boy-Scout. Simplest thing that works.
- Red-green TDD whenever possible.

## Build & Run

```sh
dotnet build dotnetclaw.slnx              # build all projects
dotnet run --project DotNetClaw/DotNetClaw.csproj  # standalone
dotnet run --project DotNetClaw.AppHost/DotNetClaw.AppHost.csproj  # Aspire (coffeeshop-cli + DotNetClaw)
```

Secrets: `dotnet user-secrets set "Slack:BotToken" "xoxb-..."` etc. See `appsettings.json` for all keys.

## Solution Structure

```
DotNetClaw/            Main ASP.NET Core app (net10.0)
DotNetClaw.AppHost/    Aspire orchestration (local dev, registers coffeeshop-cli + dotnetclaw)
DotNetClaw.ServiceDefaults/  Shared Aspire defaults (health, OTel, resilience)
DotNetClaw.Tests/      Integration tests (console runner)
```

## Architecture

```
Slack user         Browser user
    ↓ Socket Mode      ↓ HTTP POST /api/chat
SlackChannel.cs    WebChannel.cs
    └──────────┬───────┘
               ↓  sessionId + text
          ClawRuntime.cs
          ConcurrentDictionary<sessionId, AgentSession>
               ↓
          AIAgent  (MAF 1.0 + GitHub Copilot SDK 0.2.1)
          ReAct loop + streaming + tool calling
               ↓
          CopilotClient  (spawns copilot-cli via stdio JSON-RPC)
               ↓
          mind/  directory  →  system message
```

**Session IDs** — one AgentSession per unique ID:
- Slack DM: `slack:dm:{userId}`, Slack mention: `slack:{channelId}:{userId}`
- Web: `web:{cookieGuid}`

## File Map

| File | Role |
|---|---|
| `Program.cs` | Composition root — DI, AIAgent factory, channel wiring, endpoints |
| `ClawRuntime.cs` | Session store, per-session semaphore, `HandleAsync` / `HandleStreamingAsync` |
| `SlackChannel.cs` | Slack Socket Mode — DM + @mention handlers, extension methods |
| `WebChannel.cs` | HTTP chat API + static file serving, cookie-based sessions |
| `MindLoader.cs` | Loads `mind/SOUL.md` + agent files + working-memory → system message |
| `MemoryTool.cs` | Agent tools: `AppendLog`, `AddRule`, `SaveFact` (writes to `mind/.working-memory/`) |
| `ExecTool.cs` | Shell exec with safety blocklist (rm, sudo, curl, etc.) |
| `SkillLoaderTool.cs` | Skills via CLI or MCP from coffeeshop-cli (`ListSkills`, `LoadSkill`, `ListMenuItems`, `LookupCustomer`, `SubmitOrder`) |
| `wwwroot/index.html` | Browser chat UI (vanilla HTML + JS, no framework) |

## The Mind System

`mind/` is the agent's persistent identity:
- `SOUL.md` — personality and mission (always first in system message)
- `.github/agents/*.agent.md` — behavioral instructions (YAML frontmatter stripped)
- `.working-memory/memory.md` — curated facts (read every session)
- `.working-memory/rules.md` — one-liner lessons (append-only)
- `.working-memory/log.md` — raw session log (last 50 lines injected)

`MindLoader.LoadSystemMessageAsync()` joins all parts with `\n\n---\n\n`.

## Key Packages

| Package | Version | Purpose |
|---|---|---|
| `GitHub.Copilot.SDK` | 0.2.1 | CopilotClient → copilot-cli subprocess |
| `Microsoft.Agents.AI` | 1.0.0 | AIAgent, AgentSession, RunStreamingAsync |
| `Microsoft.Agents.AI.Foundry` | 1.1.0 | `AIProjectClient.AsAIAgent()` bridge for Azure AI Foundry |
| `Microsoft.Agents.AI.GitHub.Copilot` | 1.0.0-preview.260402.1 | `CopilotClient.AsAIAgent()` bridge for GitHub Copilot |
| `Azure.AI.Projects` | latest | AIProjectClient for Azure AI Foundry |
| `Azure.Identity` | latest | DefaultAzureCredential for Azure auth |
| `ModelContextProtocol` | 1.1.0 | MCP client for coffeeshop-cli bridge |
| `SlackNet` / `SlackNet.AspNetCore` | 0.17.9 | Slack Socket Mode |

## API Gotchas (verified from code)

| Expectation | Reality |
|---|---|
| Streaming emits cumulative text | SDK 0.2.1 emits **incremental deltas** — use `StringBuilder.Append()` |
| `OnPermissionRequest` is optional | **Mandatory** on all `SessionConfig` — use `PermissionHandler.ApproveAll` |
| `PermissionRequestResult.Kind` is string | It's enum `PermissionRequestResultKind` (.Approved / .Denied) |
| `AppMentionEvent` in SlackNet | Type is `AppMention` in `SlackNet.Events` |
| `UseSlackNet(c => c.UseSigningSecret(...))` | Put signing secret in `AddSlackNet(...)` — `UseSlackNet` is for socket mode |

## Adding a Channel

Each channel follows: receive message → derive `sessionId` → `runtime.HandleAsync(sessionId, text, ct)` → send reply.

Convention: extension methods `AddXxxChannel()` (DI) + `MapXxxChannel()` (endpoints) in a single file. See `SlackChannel.cs` or `WebChannel.cs`.

## Config Reference

Non-secret values in `appsettings.json`. Secrets via `dotnet user-secrets`.

| Key | Purpose |
|---|---|
| `Agent:Provider` | `"copilot"` (default) or `"foundry"` — selects the LLM backend |
| `Mind:Path` | Path to mind directory (default `./mind`) |
| `Foundry:Endpoint` / `Foundry:Model` | Azure AI Foundry project endpoint and model deployment (required when provider = `"foundry"`) |
| `Ollama:Enabled` / `Ollama:BaseUrl` / `Ollama:Model` | Optional local LLM override (Copilot provider only) |
| `Slack:BotToken` / `Slack:AppToken` / `Slack:BotUserId` | Slack credentials (user-secrets) |
| `Slack:Policy` / `Slack:AllowedUserIds` | `"open"` or `"allowlist"` with comma-separated IDs |
| `CoffeeshopCli:Mode` | `"cli"` (direct exec) or `"mcp"` (HTTP MCP bridge) |
| `CoffeeshopCli:ExecutablePath` | Path to coffeeshop-cli binary (CLI mode) |
| `CoffeeshopCli:Mcp:BaseUrl` / `Mcp:EndpointPath` | MCP server endpoint (MCP mode) |
