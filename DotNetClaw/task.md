# DotNetClaw — Task Tracker

> Tracks implementation of the 4 improvements from improve.md.
> Design details and code samples → see improve.md.
> Broader gap analysis → see gaps.md.

Status: `[x]` Done | `[~]` Partial | `[ ]` Not Started

---

## Phase 1 — Wire the Mind (Memory)

Deps: None. Can start immediately.
Files to add: `MemoryTool.cs`, `mind/.working-memory/rules.md`, `mind/.working-memory/log.md`
Files to change: `MindLoader.cs`, `mind/.github/agents/assistant.agent.md`

- [x] **1.1** Create `mind/.working-memory/rules.md` with `# Rules` header
- [x] **1.2** Create `mind/.working-memory/log.md` with `# Log` header
- [x] **1.3** Update `MindLoader.cs` to read all 3 working-memory files into system message (log.md trimmed to last 50 lines)
- [x] **1.4** Create `MemoryTool.cs` with `AppendLogAsync`, `AddRuleAsync`, `SaveFactAsync`
- [x] **1.5** Create/update `mind/.github/agents/assistant.agent.md` with memory protocol instructions
- [ ] **1.6** E2E: "Remember my favorite color is blue" → /reset → "What's my favorite color?" → agent answers "blue"

Key test from improve.md 1.6:
- Send message "Remember that my favorite color is blue" → agent calls `SaveFactAsync`
- `/reset` clears session
- Send "What's my favorite color?" → agent responds with "blue" (memory.md was reloaded into system message)

---

## Phase 2 — Give the Agent Hands (Tools + MCP)

Deps: Phase 1 (MemoryTool registered alongside other tools).
Files to add: `ExecTool.cs`, `McpToolLoader.cs`
Files to change: `Program.cs`, `DotNetClaw.csproj`, `appsettings.json`
NuGet: `ModelContextProtocol`

- [ ] **2.1** Create `ExecTool.cs` with `RunAsync` + dangerous-command blocklist
  - Test: `RunAsync("echo hello")` → `exit=0\nhello`
  - Test: `RunAsync("rm -rf /")` → "Blocked by safety filter."
  - Test: output > 8000 chars → truncated with `[truncated]` suffix

- [ ] **2.2** Create `McpToolLoader.cs` — connect to MCP servers from config, wrap tools as AITool
  - Test: Empty config returns empty list
  - Test: Valid stdio server → loads tools, logs `[MCP] Registered tool: ...`
  - Test: Unreachable server → logs warning but non-fatal

- [ ] **2.3** Add `ModelContextProtocol` NuGet package to `DotNetClaw.csproj`
  - Test: `dotnet restore && dotnet build` succeeds

- [ ] **2.4** Update `Program.cs` — register MemoryTool + ExecTool + MCP tools, pass to `AsAIAgent`
  - Test: Startup logs `Agent initialized with N tools (X built-in, Y MCP)`
  - Test: `tools` list has ≥ 4 items

- [ ] **2.5** Add `Mcp:Servers` section to `appsettings.json`
  - Test: Valid JSON after edit

- [ ] **2.6** E2E: "Run `echo hello world` in the shell" → agent calls ExecTool → replies with output
  - Also test: "List the files in the mind directory" → agent calls `ExecTool.RunAsync("ls ./mind")`

---

## Phase 3 — Channel Coverage

Deps: None (independent of Phase 1/2). Can run in parallel.
Files to add: `TeamsChannel.cs`, `TelegramChannel.cs`, `DiscordChannel.cs`
Files to change: `Program.cs`, `DotNetClaw.csproj`, `appsettings.json`, `CLAUDE.md`
NuGet: `Microsoft.Bot.Builder`, `Telegram.Bot`, `Discord.Net`

- [x] **3.2** SlackChannel.cs — exists with DM + @mention + typing indicator

- [ ] **3.1** Create `TeamsChannel.cs` (Bot Framework webhook, session: `teams:{conversationId}`)
  - Max message length: 28000 chars
  - Test: POST to `/api/messages` → agent processes and replies
  - Test: Response > 28000 chars → truncated to 27990 + ` [...]`
  - Test: Empty text → handler returns without calling agent

- [ ] **3.3** Create `TelegramChannel.cs` (polling BackgroundService, session: `telegram:{chatId}`)
  - Max message length: 4096 chars
  - Test: Startup without token → no service registered
  - Test: Startup with token → logs `[Telegram] Starting polling...`
  - Test: Message roundtrip → agent replies
  - Test: Response > 4096 chars → truncated to 4096 + `...`

- [ ] **3.4** Create `DiscordChannel.cs` (WebSocket BackgroundService, session: `discord:dm:{userId}` / `discord:{channelId}:{userId}`)
  - Max message length: 2000 chars
  - Test: Startup without token → no service registered
  - Test: DM roundtrip → agent replies
  - Test: Channel message → @mention or message→ agent replies
  - Test: Response > 2000 chars → truncated to 2000 + `...`
  - Test: Bot ignores its own messages (no infinite loop)

- [ ] **3.5** Add NuGet packages (`Microsoft.Bot.Builder`, `Telegram.Bot`, `Discord.Net`)
  - Test: `dotnet restore && dotnet build` succeeds

- [~] **3.6** Register all channels in `Program.cs`
  - Status: Slack done, Teams/Telegram/Discord pending
  - Test: All 4 channel registrations present
  - Test: Partial config → only configured channels registered

- [~] **3.7** Add channel config sections to `appsettings.json`
  - Status: Slack done, Teams/Telegram/Discord pending
  - Test: Valid JSON with all 4 channel blocks

- [~] **3.8** Update `CLAUDE.md` secrets section
  - Status: Slack done, Teams/Telegram/Discord pending
  - Add: Microsoft.Bot.Builder auth token setup, Telegram BotFather token, Discord Developer Portal token

- [ ] **3.9** E2E: agent reachable on all 4 channels with correct session ID formats
  - Send "Hello" via Teams → agent replies (session: `teams:{conversationId}`)
  - Send "Hello" via Slack DM → agent replies (session: `slack:dm:{userId}`)
  - @mention bot in Slack channel → agent replies in-thread (session: `slack:{channelId}:{userId}`)
  - Send "Hello" via Telegram DM → agent replies (session: `telegram:{chatId}`)
  - Send "Hello" via Discord DM → agent replies (session: `discord:dm:{userId}`)
  - `/status` on any channel → shows correct session ID with platform prefix
  - `/health` → `ActiveSessions` count reflects sessions from multiple channels

---

## Phase 4 — Make the Runtime Smart

Deps: Phase 1+2 for full E2E test (4.7). Chat commands work independently.
Files to change: `ClawRuntime.cs`, `Program.cs`

- [ ] **4.1** Add `/reset` command to `ClawRuntime.cs`
  - Clears session, removes from locks, returns "Session cleared. Starting fresh."
  - Test: Send `/reset` → response matches → follow-up message → no memory of previous conversation

- [ ] **4.2** Add `/status` command to `ClawRuntime.cs`
  - Returns session ID, active status, creation time, total request count
  - Test: Send `/status` → response contains all 4 fields
  - Test: Send 3 messages → `/status` → total requests shows ≥ 3

- [ ] **4.3** Add `/help` command to `ClawRuntime.cs`
  - Lists all 3 commands with descriptions
  - Test: Send `/help` → response contains `/reset`, `/status`, `/help`

- [ ] **4.4** Unknown `/xxx` commands pass through to agent (not intercepted)
  - Test: Send `/something_random hello` → agent receives full message and responds normally

- [x] **4.5** Slack typing indicator (`thinking_face` reaction)
  - Status: Already implemented in SlackChannel.cs
  - Test visual: Send Slack message → 🤔 reaction appears within 1s → disappears after reply

- [~] **4.6** `/health` endpoint with full metrics
  - Status: Basic endpoint exists at `/`, needs expansion
  - Should return: `{ Name, Status: "running", Mind: path, ActiveSessions, TotalRequests, Uptime }`
  - Test: `curl http://localhost:5000/health` → JSON with all fields
  - Test: After traffic → `TotalRequests` > 0, `ActiveSessions` ≥ 1

- [ ] **4.7** E2E: full conversation loop with commands + tools + memory
  - Send `/help` → see commands
  - "Remember I'm working on Project Alpha" → agent calls `SaveFactAsync`
  - `/status` → see session active
  - "What files are in the mind directory?" → agent calls tool
  - `/reset` → session cleared
  - "What project am I working on?" → agent says "Project Alpha" (memory.md survived reset)
  - `/health` → API returns stats showing the requests above

---

## Progress Summary

| Phase | Total | Done | Partial | Not Started |
|---|---|---|---|---|
| 1 — Wire the Mind | 6 | 0 | 0 | 6 |
| 2 — Agent Hands | 6 | 0 | 0 | 6 |
| 3 — Channels | 9 | 1 | 3 | 5 |
| 4 — Runtime Smart | 7 | 1 | 1 | 5 |
| **Total** | **28** | **2** | **4** | **22** |

---

## Key References

- [improve.md](improve.md) — Detailed design + code samples for all 4 improvements (start here for implementation)
- [gaps.md](gaps.md) — Broader 10-gap analysis (background research, not tracked)
- [CLAUDE.md](CLAUDE.md) — Build, run, secrets setup, API gotchas — updated as phases progress

## Quick Start for Implementation

1. **Phase 1 is fastest:** Create 2 files + update `MindLoader.cs` + register `MemoryTool` in `Program.cs` → agent remembers facts
2. **Phase 2 unblocks everything:** Create `ExecTool.cs` + `McpToolLoader.cs` → agent acts on world
3. **Phase 3 can run in parallel:** Each channel is ~80 lines — copy SlackChannel template, adapt for Teams/Telegram/Discord
4. **Phase 4 is polish:** Chat commands are 20 lines, enhanced `/health` is 10 lines

All steps have test criteria inline and in improve.md sections 1–4.
