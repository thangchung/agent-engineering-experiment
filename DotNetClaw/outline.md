# Build an Agent Harness with Microsoft Agent Framework and GitHub Copilot SDK

## Suggested Title Slide

**Build an Agent Harness with Microsoft Agent Framework and GitHub Copilot SDK**

Subtitle: From single-agent demos to multi-channel, session-aware, tool-enabled systems

**Azure BootCamp 2026 — Session Outline (45 min)**

---

## Short Description

Building a single AI agent is easy. Keeping it alive across channels, conversations, and tool ecosystems is the hard part. This session walks through **a production-shaped .NET 10 agent harness** built primarily with **Microsoft Agent Framework** and the **GitHub Copilot SDK**, then operationalized with **.NET Aspire**. You'll leave knowing how to design the layered "channel → runtime → agent → mind" boundary, wire persistent memory and tool surfaces, bridge CLI tools and MCP servers, and use Aspire to run and observe the whole system.

## Event Abstract

Most agent demos stop at a prompt and a response. Real systems need much more: session isolation, channel adapters, tool orchestration, memory, operational boundaries, and a clean way to connect model runtime to business capabilities. In this talk, I will break down how **a reference agent harness** uses **Microsoft Agent Framework** as the execution model, **GitHub Copilot SDK** as the model/backend bridge, and **.NET Aspire** as the operational layer around the harness. The focus is not just on calling an LLM, but on building a reusable agent harness that can survive real conversations, real tools, and real deployment concerns.

## Audience Takeaways

- Understand what an "agent harness" is and why it is a better framing than a chatbot wrapper.
- See how **Microsoft Agent Framework** and **GitHub Copilot SDK** fit together in one architecture.
- Learn a practical layering model: `channel -> runtime -> agent -> mind -> tools`.
- Learn how to add memory, skills, MCP tools, and safe execution surfaces without collapsing the design.
- See where **.NET Aspire** helps: orchestration, service discovery, observability, and local development workflows.

---

## Outline

### Part 1 — The Problem: Why "Agent Harness"? (7 min)

**What's the gap between a chatbot and an agent harness?**

- A chatbot answers messages. An agent harness *routes, isolates, persists, and equips*.
- Real-world problems a raw LLM call can't solve:
  - Multi-channel: how do you serve the same agent on Slack DMs, @mentions, Teams, Telegram, Discord?
  - Multi-session: how do you keep conversation state per-user per-channel without them bleeding into each other?
  - Identity: who *is* the agent? What are its mission, personality, and boundaries?
  - Memory: how does it remember things across days and across channels?
  - Tool surface: how do you connect it to backend capabilities without hard-coding them?
- The "harness" metaphor: channels are straps, runtime is the frame, agent is the engine, mind is the soul.

**Key insight to set up the rest of the talk:**  
> The harness contract is: every channel calls `runtime.HandleAsync(sessionId, text)` — one method, nothing else. That one constraint makes the architecture composable.

---

### Part 2 — The Core Harness Stack: MAF + Copilot SDK (8 min)

**What each SDK does and why both are needed.**

| Layer | SDK | Role |
|---|---|---|
| Agent harness core | `Microsoft.Agents.AI` (MAF) | `AIAgent`, `AgentSession`, tool calling, streaming, sessioned execution model |
| Agent model/backend bridge | `GitHub.Copilot.SDK` + `Microsoft.Agents.AI.GitHub.Copilot` | `CopilotClient`, spawns `copilot-cli` child process, surfaces as MAF `AIAgent` via `AsAIAgent()` extension |
| Channel | `SlackNet`, Bot Framework, Telegram, Discord.NET | Platform adapters, each reduces to one `HandleAsync` call |
| Operational orchestration | .NET Aspire | Multi-project launch, service discovery, OpenTelemetry, dashboard |

**Show the bridge moment — one line that connects the two SDKs:**

```csharp
return copilotClient.AsAIAgent(
    ownsClient:   true,
    id:           "agent-harness",
    tools:        tools,
    instructions: systemMessage);
```

Key gotchas worth highlighting from real debugging:
- `AsAIAgent` takes `instructions`, not `SessionConfig` — choosing the wrong overload loses your system prompt silently.
- `AgentResponseUpdate.Text` is **cumulative** (full response so far), not a delta — you must track emitted length or keep only the last update-
- `RunStreamingAsync` takes 4 params including `AgentRunOptions?`.

---

### Part 3 — The Harness: ClawRuntime + Session Isolation (8 min)

**Session IDs as the key architectural primitive.**

```
Teams user     → "teams:{conversationId}"
Slack DM       → "slack:dm:{userId}"
Slack @mention → "slack:{channelId}:{userId}"
Telegram       → "telegram:{chatId}"
Discord DM     → "discord:dm:{userId}"
```

- `ClawRuntime` = `ConcurrentDictionary<sessionId, AgentSession>` + per-session `SemaphoreSlim`.
- Why semaphore per session? `AgentSession` is not thread-safe — concurrent calls on the same session produce interleaved / garbled responses (found the hard way).
- The runtime intercepts slash commands (`/reset`, `/status`, `/help`) before they reach the agent — keeps the agent layer pure.

**Show the streaming vs. non-streaming duality:**
- `HandleStreamingAsync` — for channels that support progressive delivery (Teams, Discord).
- `HandleAsync` — for channels where you post once (Slack, Telegram). Uses the cumulative protocol quirk to grab only the last `update.Text`.

**Channel pattern** (show `SlackChannel.cs` as the reference implementation):
```
Incoming event → derive sessionId → call runtime → post reply
```
Three lines of business logic. Everything else is platform ceremony.

---

### Part 4 — The Mind: Identity, Memory, and Instructions (8 min)

**Files on disk as the agent's persistent self — inspired by msclaw.**

```
mind/
├── SOUL.md                     ← personality + mission + boundaries
├── .github/agents/
│   └── assistant.agent.md      ← behavioral instructions (YAML frontmatter stripped)
└── .working-memory/
    ├── memory.md               ← curated facts (every session, rarely written)
    ├── rules.md                ← one-liner lessons (append-only)
    └── log.md                  ← raw observations (last 50 lines injected)
```

- `MindLoader.LoadSystemMessageAsync()` joins everything with `\n\n---\n\n` → becomes the `instructions` param.
- Why files and not a database? Diff-able, version-controllable, readable as markdown.
- `Cwd = mind.MindRoot` in `CopilotClientOptions` — Copilot CLI natively discovers `.github/agents/` files; the harness and the CLI reinforce each other.
- Working memory is *injected*, not just referenced — the agent *sees* it in every session, no retrieval step.

**The three memory tools the agent can write back to:**
- `append_log` — stream-of-consciousness observations.
- `add_rule` — one-line operational lessons from mistakes.
- `save_fact` — durable cross-session facts (always read at startup).

**Demo moment:** Show a fact persisted in one Slack DM being visible in a Teams conversation 3 days later — same agent, same mind, different channel.

---

### Part 5 — Tool Surfaces: CLI, MCP, and the Dual-Mode Pattern (8 min)

**How the harness exposes domain capabilities as agent tools.**

Three registration categories in `Program.cs`:
1. **Memory tools** — always registered (`AppendLog`, `AddRule`, `SaveFact`).
2. **Skill tools** — always registered (`ListSkills`, `LoadSkill`).
3. **Domain tools** — registered conditionally based on backend mode.

**ExecTool — the CLI escape hatch:**
- Wraps shell execution with a blocklist (`rm`, `sudo`, `curl`, etc.).
- Rewrites `coffeeshop-cli <args>` to the configured full executable path.
- Enabled only when MCP is not the active mode — avoids bypassing the structured API.

**SkillLoaderTool — dual-mode pattern:**
- `Mode = cli` → shell-exec `coffeeshop-cli skills list --json` / `skills show <name>`.
- `Mode = mcp` → `McpClient` over HTTP, calls `skill_list` / `skill_show`.
- Fallback: if MCP startup probe fails, re-enable ExecTool automatically.

**MCP domain tools (new — solving the customer-lookup bug):**
- `ListMenuItemsAsync` → MCP `menu_list_items`
- `LookupCustomerAsync` → MCP `customer_lookup` (email or id)
- `SubmitOrderAsync` → MCP `order_submit`
- These are registered only when `mcpMode && mcpAvailable` — clean conditional surface.

**Show the startup probe pattern:**
```csharp
var mcpAvailable = mcpMode ? !HasTopLevelError(skillsJson) : false;
// tools conditionally assembled based on this flag
```

**Key lesson:** MCP mode on one tool doesn't constrain all agent tools. You must explicitly map mode → tool surface in the registration block.

---

### Part 6 — Aspire as Operational Glue (4 min)

**One-command local stack:**

```csharp
builder.AddProject<Projects.AgentHost>("agent-harness")
    .WithEnvironment("CoffeeshopCli__Mode", "mcp");

builder.AddProject<Projects.CoffeeshopCli>("coffeeshop-cli")
    .WithEnvironment("Hosting__EnableHttpMcpBridge", "true");
```

- Aspire is not the harness itself; it is the operational layer around the harness.
- The harness is already formed by MAF + Copilot SDK + your runtime/tool architecture.
- Aspire handles port assignment, service discovery, health check wiring, and OpenTelemetry.
- `ServiceDefaults` project adds OTLP exporter, health endpoints, and resilience policies — one `builder.AddServiceDefaults()` call in each project.
- Dashboard shows both services, their logs, traces, and metrics side-by-side.

**The full data-flow diagram:**
```
Slack ──┐
Teams ──┤ channel adapters              ┌─ MemoryTool (mind/.working-memory/)
Tg   ──┤→ ClawRuntime                  ├─ SkillLoaderTool (coffeeshop-cli / MCP)
DC   ──┘   ConcurrentDict<sessionId>  →│  AIAgent (MAF ReAct loop)
                                        │    ↕ CopilotClient (stdio JSON-RPC)
                                        └─ DomainTools (MCP: menu / customer / order)
```

---

### Part 7 — Takeaways + Q&A (2 min)

1. **One contract** — every channel reduces to `HandleAsync(sessionId, text)`.
2. **Sessions are the unit of isolation** — string IDs, `ConcurrentDictionary`, one semaphore each.
3. **Mind is files** — SOUL + agent files + working memory = the system message. No database needed.
4. **Tools are the extension points** — CLI tools, MCP servers, memory tools all compose via `AIFunctionFactory.Create()`.
5. **Dual-mode with fallback** — MCP-first, ExecTool as resilience valve, per-call fallback in skill loader.
6. **Aspire is the glue** — service defaults, discovery, and dashboard for free.

**Repo:** private / optional for this talk  
**MAF docs:** `aka.ms/agents`  
**Copilot SDK:** `github.com/github/copilot-sdk-dotnet`

---

## Suggested Demo Flow (fits within the outline above)

| When | Demo |
|---|---|
| Part 2 | Show the two SDK packages in `.csproj` and the single `AsAIAgent()` call |
| Part 3 | Live Slack DM → response, then `/reset` command clears session |
| Part 4 | Show `mind/SOUL.md` in editor; change personality line; restart; observe different tone |
| Part 5 | Switch `CoffeeshopCli:Mode` from `cli` to `mcp` in appsettings; show startup logs confirming domain tools registered; send "look up customer alice@example.com" |
| Part 6 | Open Aspire dashboard; show both services healthy; trace a Slack message through OTLP spans |

---

## Timing Budget

| Part | Topic | Minutes |
|---|---|---|
| 1 | The Problem: Why Agent Harness? | 7 |
| 2 | The Stack: MAF + Copilot SDK | 8 |
| 3 | The Harness: Runtime + Sessions | 8 |
| 4 | The Mind: Identity + Memory | 8 |
| 5 | Tool Surfaces: CLI, MCP, Dual-Mode | 8 |
| 6 | Aspire + Full Picture | 4 |
| 7 | Takeaways + Q&A | 2 |
| **Total** | | **45** |


---
Ref:
- https://github.com/github/copilot-sdk/blob/main/docs/integrations/microsoft-agent-framework.md
- https://github.com/github/copilot-sdk/blob/main/docs/auth/byok.md#ollama-local
