# DotNetClaw vs msclaw vs Polyclaw: Core Gaps Analysis

## Executive Summary

Three C#/.NET agent platforms demonstrate a clear evolution:

| Aspect | msclaw | DotNetClaw | Polyclaw | Status |
|--------|--------|-----------|---------|--------|
| **Agent Runtime** | ✅ Copilot SDK + MAF | ✅ Copilot SDK + MAF | ✅ Copilot SDK + MAF | All equal |
| **Mind Concept** | ✅ SOUL.md + working memory | ✅ SOUL.md + working memory | ✅ SOUL.md + working memory | All equal |
| **Messaging Surfaces** | ✅ SignalR hub + browser UI; M365/Teams reach via MCPorter + Agency | ⚠️ Slack + WhatsApp (2) | ✅ Teams + Telegram + Slack + Voice (9) | DotNetClaw blocked |
| **Runtime Coordination** | ✅ Per-caller reject gate + session pool + event bridge | ⚠️ Direct calls (webhook timeout only) | ⚠️ `asyncio.create_task(...)` + single locked `MessageProcessor` + proactive reply | Use polyclaw pattern for webhook channels |
| **API Gateway** | ✅ ASP.NET Core gateway (`/`, `/gateway`, `/health`, `/health/ready`, `/v1/responses`) | ❌ None | ✅ Full aiohttp + middleware | DotNetClaw critical gap |
| **Background Jobs / Cron** | ✅ Hosted `CronEngine` + tool surface + isolated prompt/command executors | ❌ None | ✅ Scheduler + cron expressions | DotNetClaw critical gap |
| **Tunneling** | ✅ Built-in Dev Tunnel integration (`MsClaw.Tunnel`) | ❌ None | ✅ Cloudflare tunnel | Low priority |
| **Session Persistence** | ❌ Memory | ❌ Memory | ✅ JSONL store | Medium priority |
| **Response API** | ✅ OpenResponses `POST /v1/responses` (JSON + SSE) | ❌ None | ❌ No OpenResponses surface | DotNetClaw medium gap |
| **Admin / Operator Surface** | ⚠️ Basic operator surface (chat UI, session ops, health, tunnel status) | ❌ None (plan: static chat UI + session panel) | ✅ /api/setup/* + /api/sessions/* | Medium priority |
| **Security (HITL/AITL/Sandbox)** | ❌ None | ❌ None | ✅ Yes | Low priority |
| **Voice** | ❌ None | ❌ None | ✅ Azure ACS + OpenAI Realtime | Low priority |
| **Web UI** | ✅ Static chat UI | ❌ None (recommend static HTML/JS, not Blazor) | ✅ React SPA | Medium priority |

Citation tags used below: `[C1]...[C11]` (full links and file refs in `References`).

---

## Core Architectural Problem

DotNetClaw channels call `ClawRuntime.HandleAsync()` synchronously from the event handler. [C1][C2]

**What DotNetClaw already has (no changes needed):**
- `SemaphoreSlim(1,1)` per `sessionId` in `ClawRuntime` — this is polyclaw's `asyncio.Lock`, already implemented [C11]
- Concurrent cross-session processing works today (separate sessions run in parallel)
- Slack Socket Mode path currently awaits runtime directly in handlers. [C2]

**The only real gap (narrow):** webhook channels (Teams ≤15s, WhatsApp/Twilio ≤15s) will timeout if the agent is slow. *Neither channel is implemented yet.* When they are added, the handler must return `200` immediately and reply proactively.

**Polyclaw's solution applied to C# (~20 LOC per webhook channel):** [C11]

```
Webhook handler → acknowledge 200 immediately
               → _ = Task.Run(...)  // fire-and-forget
               → ClawRuntime.HandleAsync(sessionId, text)  // existing SemaphoreSlim(1,1) serializes per session
               → proactive reply via stored conversation reference
```

---

## Core Components Needed (Priority Order)

### 1. **Runtime Coordination: Polyclaw Pattern for Webhook Channels**

**Current state:** `ClawRuntime` already has `ConcurrentDictionary<string, SemaphoreSlim>` with `SemaphoreSlim(1,1)` per `sessionId` — this IS polyclaw's `asyncio.Lock`. Concurrent cross-session processing works today. No broker needed. [C11]

**What is missing:** webhook channels (Teams, Twilio) must return `200` before the agent finishes. The fix is ~20 LOC per webhook handler.

| Approach | LOC | Net parallelism gain | Verdict |
|----------|-----|---------------------|---------|
| `_ = Task.Run(...)` + existing `SemaphoreSlim` (polyclaw pattern) | ~20 per channel | None needed (lock already serializes) | ✅ Use this [C11] |
| Priority Queue + Worker Pool broker | ~150+ | None (workers still block on same `SemaphoreSlim(1,1)`) | Only for multi-process / durable delivery |

**Implementation (add when Teams or Twilio channel is added):**

```csharp
// Webhook channel handler — acknowledge immediately, reply proactively
public async Task<IActionResult> OnMessageAsync(string sessionId, string text,
    Func<string, Task> proactiveReplySender, CancellationToken ct)
{
    _ = Task.Run(async () =>
    {
        // SemaphoreSlim(1,1) in ClawRuntime already serializes per sessionId
        var response = await _runtime.HandleAsync(sessionId, text, CancellationToken.None);
        await proactiveReplySender(response);
    }, ct);

    return Ok();  // Return 200 before agent finishes
}
```

**Estimated effort:** 1 day per webhook channel (implement when channel is added)

---

### 2. **Static Chat UI + Session Management (msclaw-style, no Blazor)**

**Decision:** use a lightweight static HTML/JS chat page served by ASP.NET Core static files (same direction as msclaw), not Blazor. [C3][C7]

**Why this is Priority 2:**
- Gives immediate operator visibility (session list, chat input/output, manual testing)
- Reuses the same backend APIs planned in Priority 3 (`/api/chat/send`, `/api/sessions/{id}`)
- Lowest maintenance surface (no component lifecycle/state complexity)

**Scope (MVP):**
- Single page in `wwwroot` (e.g., `chat.html` + `chat.js` + `chat.css`)
- Session actions: create/select session ID, view active session, clear local view
- Chat actions: send prompt, show assistant reply, append timestamped transcript
- API usage: `POST /api/chat/send`, `GET /api/sessions/{sessionId}`

**Non-goals (explicit):**
- No Blazor, no SPA framework, no auth/roles in MVP
- No realtime streaming required in v1 (request/response is enough)

**Estimated effort:** 1-2 days

---

### 3. **API Gateway + Command Dispatcher** (CRITICAL - blocks /api/* endpoints)

**Problem:** No HTTP API; channels can't call REST endpoints; no `/reset`, `/status` command handling.

**What msclaw already proves**

- A single ASP.NET Core gateway binary is enough.
- Static UI, SignalR hub, readiness probes, and OpenResponses can share one runtime.
- The thin coordination layer is small: `AgentMessageService` + `IConcurrencyGate` + `SessionPool`.
- Per-caller conflict rejection is good enough for v1; queueing is optional later.

**Simple Solution (ASP.NET Core Minimal APIs)**

```csharp
// 1. Command dispatcher (no channel awareness)
public sealed class CommandDispatcher(SessionStore sessionStore, ILogger<CommandDispatcher> logger)
{
    public async Task<string?> TryDispatchAsync(string sessionId, string text, CancellationToken ct)
    {
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || !tokens[0].StartsWith("/"))
            return null;
        
        return tokens[0][1..].ToLower() switch
        {
            "reset" => await HandleResetAsync(sessionId, ct),
            "status" => await HandleStatusAsync(sessionId, ct),
            "help" => "Commands: /reset, /status, /help, /schedule <task> <interval>",
            _ => null
        };
    }

    private async Task<string> HandleResetAsync(string sessionId, CancellationToken ct)
    {
        logger.LogInformation("[Commands] {SessionId} reset", sessionId);
        // In a real app, clear session state
        return $"Session {sessionId} reset.";
    }

    private async Task<string> HandleStatusAsync(string sessionId, CancellationToken ct)
    {
        var history = await sessionStore.LoadSessionAsync(sessionId, ct);
        return $"Session {sessionId}\nMessages: {history.Count}";
    }
}

// 2. Minimal API endpoints
app.MapPost("/api/chat/send", async (string sessionId, string text, ClawRuntime runtime, CommandDispatcher dispatcher, CancellationToken ct) =>
{
    // Try command first
    var cmdResp = await dispatcher.TryDispatchAsync(sessionId, text, ct);
    if (cmdResp != null) 
        return Results.Ok(new { response = cmdResp });
    
    // Runtime already serializes per session with SemaphoreSlim(1,1)
    var response = await runtime.HandleAsync(sessionId, text, ct);
    return Results.Ok(new { response });
});

app.MapGet("/api/sessions/{sessionId}", async (string sessionId, SessionStore store, CancellationToken ct) =>
{
    var history = await store.LoadSessionAsync(sessionId, ct);
    return Results.Ok(new { sessionId, messageCount = history.Count });
});

app.MapGet("/api/runtime/stats", (ClawRuntime runtime) =>
{
    // Expose runtime stats when available (sessions, active turns, etc.)
    return Results.Ok(new { status = "ok" });
});

app.MapPost("/api/schedules", async (ScheduledJob job, SchedulerStore store, CancellationToken ct) =>
{
    await store.SaveJobAsync(job, ct);
    return Results.Created($"/api/schedules/{job.Id}", job);
});
```

**Benefits:**
- ✅ Channels can call REST endpoints instead of direct method calls
- ✅ `/reset`, `/status`, `/help` handled before agent sees them
- ✅ Session visibility (`GET /api/sessions/{id}`)
- ✅ No external dependencies (built-in ASP.NET Core minimal APIs)

**Estimated effort:** 1-2 days

---

### 4. **Session Persistence** (MEDIUM PRIORITY)

**Problem:** In-memory sessions lost on restart.

**Simple Solution (JSONL Format)**

```csharp
public sealed class SessionStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task SaveMessageAsync(
        string sessionId,
        string role,  // "user" | "assistant"
        string content,
        CancellationToken ct = default)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            session_id = sessionId,
            role,
            content
        }) + "\n";

        await _gate.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_path, line, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<(string role, string content)>> LoadSessionAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return new();

        var messages = new List<(string, string)>();
        var lines = await File.ReadAllLinesAsync(_path, ct);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            try
            {
                var doc = JsonDocument.Parse(line);
                if (doc.RootElement.GetProperty("session_id").GetString() == sessionId)
                {
                    var role = doc.RootElement.GetProperty("role").GetString()!;
                    var content = doc.RootElement.GetProperty("content").GetString()!;
                    messages.Add((role, content));
                }
            }
            catch { /* skip malformed */ }
        }

        return messages;
    }
}
```

**Files added:** 
- `State/SessionStore.cs` (~80 LOC)

**Estimated effort:** 0.5 days

---

### 5. **Cron-Based Background Jobs** (MEDIUM PRIORITY)

**Problem:** No scheduled automation; scheduler tasks blocked on channel latency.

**What msclaw already proves**

- Cron can live as a hosted service inside the gateway.
- Job CRUD can be exposed as tools instead of a separate UI first.
- Persisted jobs do not need a database; msclaw uses JSON files plus an in-memory cache.
- Two executor types are enough for a lean v1: prompt jobs and command jobs.

**Simple Solution (BackgroundService + Cron Expressions)**

```csharp
public record ScheduledJob(
    string Id,
    string Prompt,
    string CronExpression,  // "0 2 * * *" = 2 AM daily
    bool Enabled,
    DateTime? LastRunTime = null
);

public sealed class SchedulerService(
    ClawRuntime runtime,
    SchedulerStore store,
    ILogger<SchedulerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await CheckAndExecuteJobsAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[Scheduler] Error");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }

    private async Task CheckAndExecuteJobsAsync(CancellationToken ct)
    {
        var jobs = await store.ListJobsAsync(ct);
        var now = DateTime.UtcNow;

        foreach (var job in jobs.Where(j => j.Enabled))
        {
            // Simplified cron check (use Cronos NuGet for production)
            if (ShouldRunNow(job, now))
            {
                var sessionId = $"scheduler::{job.Id}";
                var response = await runtime.HandleAsync(sessionId, job.Prompt, ct);
                logger.LogInformation("[Scheduler] Executed job {Id}. Response preview: {Preview}",
                    job.Id,
                    response[..Math.Min(50, response.Length)]);
                
                var updatedJob = job with { LastRunTime = now };
                await store.SaveJobAsync(updatedJob, ct);
            }
        }
    }

    private bool ShouldRunNow(ScheduledJob job, DateTime now)
    {
        // Parse cron or use simple heuristic
        // For real: use Cronos NuGet
        return true;  // TODO: implement cron parsing
    }
}

// Register in Program.cs
builder.Services.AddSingleton<SchedulerStore>();
builder.Services.AddHostedService<SchedulerService>();
```

**Dependencies:** `Cronos` NuGet (lightweight cron parser, 60 KB)

**Files added:**
- `Scheduler/SchedulerService.cs` (~100 LOC)
- `Scheduler/SchedulerStore.cs` (~150 LOC)

**Estimated effort:** 1.5 days

---

### 6. **Tunneling** (LOW PRIORITY - nice-to-have)

**Problem:** Teams/Telegram webhooks require public HTTPS; local dev needs tunnel.

**What msclaw already proves**

- Built-in tunnel lifecycle is possible and clean in .NET.
- `--tunnel` plus a hosted service is enough.
- Tunnel state can be exposed via a simple `/api/tunnel/status` endpoint.

**Recommended DotNetClaw solution: external tunnel first, hosted service later**

```bash
# Option A: ngrok (free, but URL changes on restart)
ngrok http 5000

# Option B: Microsoft Dev Tunnel (closer to msclaw's built-in approach)
devtunnel host -p 5000

# In Teams registration:
# Messaging endpoint: https://<your-tunnel>.ngrok.io/api/messages
```

**No code changes needed for MVP** — just tunnel local port 5000 to public URL.

If DotNetClaw later needs a built-in tunnel, msclaw's split is the right model: separate tunnel library + hosted service + status endpoint.

**Estimated effort:** 0.5 days (setup only)

---

### 7. **Response API** (MEDIUM PRIORITY)

**Problem:** Agent response isn't immediately returned; channels need async delivery.

**Current Flow (blocking):**
```
Channel → Agent → Response → Channel.Send()  [Agent response lost if channel times out]
```

**Improved Flow (async):**
```
Channel/Webhook → Ack 200 fast → Background task → Agent → ResponseStore + proactive send
```

**Simple Solution (Store + Async Delivery)**

```csharp
// 1. Response store (in-memory or JSONL)
public sealed class ResponseStore
{
    private readonly ConcurrentDictionary<string, string> _responses = new();

    public void StoreResponse(string requestId, string response)
    {
        _responses[requestId] = response;
    }

    public bool TryGetResponse(string requestId, out string response)
    {
        return _responses.TryRemove(requestId, out response!);
    }
}

// 2. Webhook handler: return fast, process in background
app.MapPost("/api/webhook/messages", (WebhookRequest req, ClawRuntime runtime, ResponseStore store) =>
{
    var requestId = Guid.NewGuid().ToString("N");

    _ = Task.Run(async () =>
    {
        var response = await runtime.HandleAsync(req.SessionId, req.Text, CancellationToken.None);
        store.StoreResponse(requestId, response);
    });

    return Results.Accepted($"/api/responses/{requestId}", new { requestId });
});

// 3. Caller polls for async response
app.MapGet("/api/responses/{requestId}", (string requestId, ResponseStore store) =>
{
    if (store.TryGetResponse(requestId, out var response))
        return Results.Ok(new { response });

    return Results.NotFound();
});
```

**Benefits:**
- ✅ Channels don't block on agent response
- ✅ Response can be delivered later (WhatsApp, Telegram)
- ✅ Proactive notifications possible
- ✅ No external dependencies

**Estimated effort:** 0.5 days

---

## CLI-Based MCP Access: mcp-cli vs MCPorter + Agency

Both tools solve the same core problem from `agent-genesis`: **avoid loading full MCP schemas as always-on sidecars**. Instead, teach the agent CLI call patterns and invoke tools only when needed. They differ significantly in scope.

### philschmid/mcp-cli — recommended for DotNetClaw

[`mcp-cli`](https://github.com/philschmid/mcp-cli) is a lightweight, Bun-based single binary that bridges to **any** MCP server. [C4]

**Critical advantage for DotNetClaw: it reads from `mcp_servers.json` directly — the same config file DotNetClaw already has.** No extra setup. All existing servers (markitdown, microsoft-docs, aspire, playwright, context7, and any future additions) work immediately.

| Feature | Detail |
|---------|--------|
| Config | `mcp_servers.json` — **same file DotNetClaw already uses** |
| Servers | Any stdio or HTTP MCP server |
| Auth | None required beyond server config |
| Daemon | Built-in Unix socket pooling, 60s idle timeout |
| Tool filtering | `allowedTools`/`disabledTools` glob per server |
| Retry | Automatic exponential backoff for transient errors |
| Agent integration | Comes with `SKILL.md` — drop into `mind/.github/skills/mcp-cli/` |
| Install | Single binary (`curl install.sh`) or `bun install -g` |

**Commands the agent learns once in instructions:**

```bash
mcp-cli                                         # list all servers + tools
mcp-cli info <server>                           # show server tools + params
mcp-cli info <server> <tool>                    # get JSON schema
mcp-cli grep "*file*"                           # search tools by glob
mcp-cli call <server> <tool> '{"key":"value"}' # call tool
echo '{"path":"./x"}' | mcp-cli call fs read_file  # stdin for complex args
```

**DotNetClaw integration is zero-config:**  
`mcp-cli` discovers `mcp_servers.json` in the working directory automatically. The ExecTool or a trusted tool wrapper can call it. Add the SKILL.md to `mind/.github/skills/mcp-cli/SKILL.md` and the agent knows the full workflow.

---

### MCPorter + Agency — for M365/Teams specifically

[MCPorter](https://github.com/steipete/mcporter) + [Agency](https://aka.ms/agency) is the right choice **if and only if** Teams, Mail, Calendar, or ADO integration is specifically wanted. It uses a separate `~/.mcporter/mcporter.json` and requires a Microsoft work/school account with Entra ID auth. [C5][C6]

| Feature | Detail |
|---------|--------|
| Config | `~/.mcporter/mcporter.json` (separate from DotNetClaw's config) |
| Servers | Agency-provided M365 servers only (Teams, Mail, Calendar, ADO, etc.) |
| Auth | Microsoft account + Agency CLI required |
| Daemon | `lifecycle: "keep-alive"` daemon mode |
| Best for | Teams inbox polling, proactive replies, Mail/Calendar access |

**Only add MCPorter if a Teams operator-inbox pattern is needed** (e.g., mention monitoring similar to the `switchboard` pattern from `agent-genesis`).

---

### Recommendation

| Use case | Tool |
|----------|------|
| Access existing DotNetClaw MCP servers (aspire, playwright, context7, etc.) | ✅ **mcp-cli** |
| Call GitHub, filesystem, database, or any other MCP server | ✅ **mcp-cli** |
| Teams message read/post/monitor | ✅ **MCPorter + Agency** |
| Mail/Calendar/ADO integration | ✅ **MCPorter + Agency** |
| Neither — runtime coordination pattern, session store, API gateway | ❌ Neither replaces these |

**Phase 1 action:** Install `mcp-cli`, add `SKILL.md` to the mind, wire a trusted `ExecTool` call. Zero new config needed.  
**Phase 2 (optional):** Add MCPorter + Agency only if Teams inbox/proactive-reply patterns are wanted.

---

## Implementation Roadmap

**Phase 0** (2-3 weeks): Core infrastructure
- Webhook async coordination pattern (`Task.Run` + proactive reply) for Teams/Twilio handlers (1 day per channel, when added)
- Static chat UI + session management page (HTML/JS, 1-2 days)
- API gateway + command dispatcher (2 days)
- Session persistence (0.5 days)
- Tests + integration (2 days)

**Phase 1** (1 week): Background automation
- Scheduler service (1.5 days)
- Tunneling setup (0.5 days)
- Response API (0.5 days)

**Phase 2** (1 week): Optional enhancements
- Admin dashboard polish (filters, export, health cards, 2 days)
- Security (HITL/AITL, 2 days)
- Observability (/api/sessions + webhook processing metrics, 1 day)

**Total: ~20-25 days for MVP (core only)**

---

## Lessons from msclaw → Polyclaw Evolution

| Lesson | msclaw Approach | Polyclaw Solution | DotNetClaw Should |
|--------|-----------------|-------------------|-------------------|
| **Agent Identity** | Files (SOUL.md) | ✅ Kept files | ✅ Keep files |
| **Session Isolation** | SessionId per platform | ✅ Kept format | ✅ Keep format |
| **Gateway Surface** | ASP.NET Core daemon + SignalR + OpenResponses + static UI | aiohttp + admin/runtime APIs | Start with one ASP.NET Core host |
| **Extensibility** | Tool bridge + cron tools; mcp-cli for general MCP; MCPorter + Agency for M365 | MCP servers + plugins | Use mcp-cli first (zero-config with existing mcp_servers.json) |
| **Multi-channel** | Browser first; external platforms through MCPorter/Agency or channel plans | 9 channels + Bot Framework | Start with gateway first, channels second |
| **Background Jobs** | Hosted cron engine + prompt/command executors | Cron scheduler | Add in Phase 1 |
| **Persistence** | Memory | JSONL sessions + stores | Add in Phase 0 |
| **Response API** | OpenResponses over HTTP + SSE | Proactive replies for bot channels | Add `/v1/responses` early |
| **Tunnel** | Built-in Dev Tunnel library + hosted service | Cloudflare tunnel | Use external tunnel first, inline later if needed |
| **Message Decoupling** | Per-caller gate + session pool + event bridge | Background task + locked processor + proactive reply | Use background task handoff only for webhook channels |

---

## Architecture Comparison Table

| Component | msclaw | DotNetClaw Today | + Phase 0 | + Polyclaw |
|-----------|--------|-----------------|-----------|-----------|
| **Agent Loop** | ✅ Copilot SDK | ✅ Copilot SDK | ✅ Same | ✅ Same |
| **Mind Files** | ✅ SOUL.md | ✅ SOUL.md | ✅ Same | ✅ Same |
| **Channels / Surfaces** | ✅ Browser UI + SignalR + OpenResponses + optional Teams path | ⚠️ Slack, WhatsApp | ⚠️ +API | ✅ 9 channels |
| **Runtime Coordination** | ✅ Per-caller reject gate + session pool + event bridge | ⚠️ Direct calls (safe for Slack, not webhook-safe) | ✅ Keep direct calls for Socket Mode + webhook async handoff | ✅ Background task handoff |
| **Concurrency Model** | 1 active run per caller, reject mode | 1 locked turn per session (`SemaphoreSlim`) | Same model; add webhook fast-ack path | 1 locked processor per bot pipeline |
| **Session Store** | In-memory pooled sessions | Memory | JSONL | JSONL |
| **API Gateway** | ✅ ASP.NET Core gateway | ❌ None | ✅ Minimal APIs | ✅ Full aiohttp |
| **Response API** | ✅ OpenResponses JSON/SSE | ❌ None | ✅ `/v1/responses` | ❌ Not core |
| **Command Dispatch** | Session ops + hub methods, not slash-command oriented | None | ✅ /reset, /status | ✅ /reset, /status, /plugin |
| **Background Jobs** | ✅ CronEngine + CronToolProvider + prompt/command executors | ❌ None | ✅ Cron scheduler | ✅ Full cron |
| **Tunnel** | ✅ Dev Tunnel hosted service | ❌ None | ⚠️ External first | ✅ Cloudflare tunnel |
| **Web UI** | ✅ Static HTML/JS | ❌ None | ✅ Static HTML/JS chat + session panel | ✅ React SPA |
| **Observability** | Health + readiness + tunnel status | ILogger | ILogger + /api/stats | OTel tracing |
| **Security** | None | None | None | HITL/AITL/Sandbox |

---

## Key Insights

1. **Broker is not foundational for this codebase today** — DotNetClaw already has per-session locking in `ClawRuntime`; webhook fast-ack handoff solves the real timeout risk with far less code.

2. **msclaw is no longer just the Mind concept** — it now proves a lean .NET gateway shape: ASP.NET Core host, SignalR, OpenResponses, static UI, per-caller concurrency gate, hosted cron, and built-in dev tunnel.

3. **Polyclaw optimization is async delivery, not queueing** — Bot Framework webhooks timeout after 15 seconds; polyclaw avoids this with background task creation, a single locked processor, and proactive messaging. [C11]

4. **Simplicity still wins** — msclaw's gateway stays lean by using ASP.NET Core, static files, JSON-backed cron state, and a reject-mode concurrency gate rather than a distributed queue or a heavy admin backend.

5. **Tunnel should stay low-priority for DotNetClaw** — msclaw proves built-in dev tunnel hosting is viable, but external tunnel tooling is still the simpler first step for this codebase.

---

## Summary: DotNetClaw Path Forward

**Must have (Phase 0):**
1. ✅ API gateway (minimal APIs)
2. ✅ Static chat UI + session management (HTML/JS, msclaw-style, no Blazor)
3. ✅ Command dispatcher (/reset, /status)
4. ✅ Session persistence (JSONL)
5. ✅ Webhook async handoff pattern (only when Teams/Twilio webhook channels are added)

**Should have (Phase 1):**
6. ✅ Scheduler service (cron jobs)
7. ✅ Response API (`/v1/responses` with JSON first, SSE next)
8. ⚠️ Tunneling (use external tooling first; copy msclaw's hosted tunnel only if it becomes operationally necessary)

**Nice to have (Phase 2+):**
9. ⚠️ Admin dashboard polish and advanced operators tools
10. ⚠️ Security features (HITL/AITL)
11. ⚠️ Observability in depth

**Total estimated effort for MVP (core only):** **20-25 days** with a team of 2-3 engineers, or **5-6 weeks** solo.

---

## References

- `[C1]` `DotNetClaw/ClawRuntime.cs` — runtime request path, per-session `SemaphoreSlim(1,1)`, and `HandleAsync`/`HandleStreamingAsync` behavior.
- `[C2]` `DotNetClaw/SlackChannel.cs` — Slack Socket Mode handlers and direct `await runtime.HandleAsync(...)` usage.
- `[C3]` `CLAUDE.md` — architecture notes describing lightweight gateway and UI direction in this repo context.
- `[C4]` `https://github.com/philschmid/mcp-cli` — mcp-cli capabilities, config model, and usage.
- `[C5]` `https://github.com/steipete/mcporter` — MCPorter behavior and M365-oriented bridging.
- `[C6]` `https://aka.ms/agency` and `https://github.com/ianphil/agent-genesis/blob/main/capabilities/mcporter-agency.md` — Agency + MCPorter pattern context.
- `[C7]` `https://github.com/ianphil/msclaw` — msclaw reference for static/operator UI direction.
- `[C8]` `DotNetClaw/Program.cs` — current host wiring and endpoint composition.
- `[C9]` `DotNetClaw/mcp_servers.json` — existing MCP server configuration used in recommendations.
- `[C10]` `README.md` — project usage and setup context for planning assumptions.
- `[C11]` `https://github.com/ianphil/polyclaw` — Polyclaw runtime pattern reference (background task handoff + per-conversation lock + proactive reply model).
