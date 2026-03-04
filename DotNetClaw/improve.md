# DotNetClaw — Improvement Plan

> A better, simpler approach than msclaw's bootstrap — drawing lessons from OpenClaw, nanobot, and msclaw, but designed for our stack: **GitHub Copilot SDK** + **Microsoft Agent Framework (MAF)** + **MCP**.
>
> Philosophy: msclaw splits the mind into 5 C# classes and 10+ directories. OpenClaw needs 430k LOC. nanobot keeps it lean at ~4k LOC. We go leaner — **4 focused improvements** that transform DotNetClaw from "chat-only, volatile" to "acts on the world, remembers everything, reachable everywhere, extensible via MCP."

---

## Design Principles (What We Take & What We Skip)

### From msclaw (take the idea, simplify execution)
- **Three-file working memory** (memory.md, rules.md, log.md) — but we skip the MindValidator, MindScaffold, MindReader, EmbeddedResources overhead. Our `MindLoader` already works; we just need to make it read more files and let the agent write to them.
- **Session handover discipline** — the agent writes decisions + next steps to log.md at session end. Great idea. We encode this in the agent instructions, not in C# code.
- **Bootstrap concept** — instead of msclaw's embedded template system with 3 interview phases, we use a simpler "first-run detection": if SOUL.md is generic/empty, the agent initiates a short identity conversation. No scaffold class needed.

### From nanobot (tools that actually work)
- **Built-in tools as plain functions** — nanobot's `shell.py`, `filesystem.py`, `web.py` are just decorated Python functions. We do the same: C# methods with `[Description]` attributes, registered via `AIFunctionFactory.Create()`. Zero framework overhead.
- **Dangerous command filter** — simple blocklist. No Docker sandbox (overkill for a personal bot).
- **MCP as first-class tool source** — nanobot calls MCP servers natively (stdio + HTTP). We do the same via the official `ModelContextProtocol` C# SDK, but MAF gives us the agent loop for free.

### From OpenClaw (architecture patterns, not scale)
- **Session persistence as JSONL** — simple, appendable, human-readable. We don't need SQLite.
- **Chat commands** (`/reset`, `/status`, `/help`) — intercepted before the agent loop. Clean separation.
- **Typing indicators** — Slack reaction-based thinking indicator. Small UX win, big perceived quality.

### What we deliberately skip
- msclaw's `MindScaffold` (creates 10 directories) — overkill. We scaffold with `mkdir -p` in the README.
- msclaw's `MindValidator` (93 lines of checks) — SOUL.md already throws FileNotFoundException. Good enough.
- msclaw's `MindReader` (git pull sync) — premature for a personal bot. Add later if mind becomes a git repo.
- msclaw's `EmbeddedResources` (compile templates into DLL) — we keep templates as files in `mind/`. Simpler to edit.
- OpenClaw's plugin SDK / ClawHub — we have MCP. It's the industry standard now.
- nanobot's subagent spawning — MAF doesn't support multi-agent yet in this version. Add when MAF ships it.

---

## The Four Improvements

### Improvement 1 — Wire the Mind (Make the Agent Remember)

**Problem:** `MindLoader.LoadSystemMessageAsync()` reads SOUL.md + agent files but ignores `.working-memory/`. The agent file *tells* the agent to read memory, but the memory is never injected into the system prompt. The agent is amnesiac.

**Solution:** Expand `.working-memory/` from 1 file to 3 (msclaw pattern), inject all three into the system message, and give the agent tools to write to them.

#### Mind directory (target state)

```
mind/
├── SOUL.md                          ← who you are (personality, mission, boundaries)
├── .github/agents/
│   └── assistant.agent.md           ← how you operate (instructions, memory protocol)
└── .working-memory/
    ├── memory.md                    ← curated facts (read every session, rarely updated)
    ├── rules.md                     ← lessons from mistakes (append when learning)
    └── log.md                       ← raw session log (append each session, consolidate → memory.md)
```

#### Why 3 files instead of 1?

| File | Analogy | Write frequency | Read frequency |
|---|---|---|---|
| `memory.md` | Long-term memory | Rare (consolidation only) | Every session start |
| `rules.md` | Instincts / reflexes | When mistakes happen | Every session start |
| `log.md` | Stream of consciousness | Every session | During consolidation |

nanobot's `memory.py` mixes all three concerns into one file — it gets cluttered fast. msclaw's separation is genuinely better. We keep it.

#### MindLoader changes

```csharp
// ── MindLoader.cs — updated LoadSystemMessageAsync ─────────────────────

public async Task<string> LoadSystemMessageAsync(CancellationToken ct = default)
{
    var soulPath = Path.Combine(_mindRoot, "SOUL.md");
    if (!File.Exists(soulPath))
        throw new FileNotFoundException(
            $"SOUL.md not found at {Path.GetFullPath(soulPath)}. " +
            "Create the mind directory structure first.");

    var soul = await File.ReadAllTextAsync(soulPath, ct);

    // Agent instruction files (.github/agents/*.agent.md)
    var agentsDir = Path.Combine(_mindRoot, ".github", "agents");
    var parts = new List<string> { soul };

    if (Directory.Exists(agentsDir))
    {
        foreach (var f in Directory.GetFiles(agentsDir, "*.agent.md")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var content = await File.ReadAllTextAsync(f, ct);
            parts.Add(StripFrontmatter(content));
        }
    }

    // ── NEW: Inject working memory into system message ──────────────
    // This is the critical change. The agent's "memory" is only real
    // if it's in the context window. These files are small (< 2KB each
    // typically) so the token cost is trivial.
    var wmDir = Path.Combine(_mindRoot, ".working-memory");
    if (Directory.Exists(wmDir))
    {
        var memoryFiles = new[] { "memory.md", "rules.md", "log.md" };
        var memoryParts = new List<string>();

        foreach (var fileName in memoryFiles)
        {
            var path = Path.Combine(wmDir, fileName);
            if (!File.Exists(path)) continue;

            var content = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(content)) continue;

            // Trim log.md to last 50 lines to control token usage
            if (fileName == "log.md")
            {
                var lines = content.Split('\n');
                if (lines.Length > 50)
                    content = string.Join('\n', lines[^50..]);
            }

            memoryParts.Add(content);
        }

        if (memoryParts.Count > 0)
        {
            parts.Add("## Working Memory\n\n" +
                       "These are your persistent files. They survive across sessions.\n\n" +
                       string.Join("\n\n---\n\n", memoryParts));
        }
    }

    return string.Join("\n\n---\n\n", parts);
}
```

#### MemoryTool — let the agent write to its own brain

```csharp
// ── MemoryTool.cs (new file) ───────────────────────────────────────────
using System.ComponentModel;

namespace DotNetClaw;

/// <summary>
/// Agent tools for persistent working memory.
///
/// Design choice: separate read/write per file rather than one generic
/// "write to any file" tool. This constrains the agent to the 3-file
/// protocol and prevents it from creating random files in .working-memory/.
///
/// Why tools instead of just injecting the files?
/// Reading is automatic (MindLoader injects at session start).
/// Writing requires tools because the agent decides WHEN to write.
/// </summary>
public sealed class MemoryTool(string mindRoot)
{
    private readonly string _wmDir = Path.Combine(Path.GetFullPath(mindRoot), ".working-memory");

    // ── Log (append-only, raw observations) ──────────────────────────

    [Description("Append an observation to the session log. Use for: decisions made, " +
                 "things learned, session handover notes. This is your raw stream of " +
                 "consciousness — write freely, consolidate later.")]
    public async Task<string> AppendLogAsync(
        [Description("The log entry to append")] string entry,
        CancellationToken ct = default)
    {
        var path = Path.Combine(_wmDir, "log.md");
        Directory.CreateDirectory(_wmDir);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        await File.AppendAllTextAsync(path, $"- [{timestamp}] {entry}\n", ct);
        return "Logged.";
    }

    // ── Rules (append-only, lessons from mistakes) ───────────────────

    [Description("Add an operational rule learned from a mistake or discovery. " +
                 "Format: one-liner that prevents the mistake from recurring. " +
                 "Example: 'Always check if file exists before reading.'")]
    public async Task<string> AddRuleAsync(
        [Description("The rule to add (one-liner)")] string rule,
        CancellationToken ct = default)
    {
        var path = Path.Combine(_wmDir, "rules.md");
        Directory.CreateDirectory(_wmDir);

        // Ensure header exists
        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, "# Rules\n\n", ct);

        await File.AppendAllTextAsync(path, $"- {rule}\n", ct);
        return $"Rule added: {rule}";
    }

    // ── Memory (curated, rewritten during consolidation) ─────────────

    [Description("Save or update a fact in long-term memory. Use sparingly — only " +
                 "for important, durable facts (user preferences, project context, " +
                 "key dates). This file is read at the start of every session.")]
    public async Task<string> SaveFactAsync(
        [Description("The fact to remember")] string fact,
        CancellationToken ct = default)
    {
        var path = Path.Combine(_wmDir, "memory.md");
        Directory.CreateDirectory(_wmDir);

        if (!File.Exists(path))
            await File.WriteAllTextAsync(path, "# Working Memory\n\n", ct);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd");
        await File.AppendAllTextAsync(path, $"- [{timestamp}] {fact}\n", ct);
        return $"Remembered: {fact}";
    }
}
```

#### Updated agent instructions

```markdown
<!-- mind/.github/agents/assistant.agent.md — enhanced version -->
---
description: Main assistant — behavioral instructions for DotNetClaw
---

## Context Awareness
You are a messaging chatbot (Teams + Slack). Keep replies SHORT.
- Plain text preferred. Avoid markdown tables or code blocks.
- Emojis sparingly. Hyphens for lists.

## Memory Protocol

Your memory lives in `.working-memory/` — three files, each with a purpose:

| File | Purpose | When to write |
|---|---|---|
| memory.md | Curated facts (user prefs, project context, key dates) | Rarely — only durable facts |
| rules.md | One-liner lessons from mistakes | When you make a mistake or learn a convention |
| log.md | Raw session observations | Every session — decisions, discoveries, handover notes |

**Session start:** Your memory is already in your context (injected automatically).
**During session:** Use `AppendLog` freely for observations. Use `AddRule` when you learn something.
Use `SaveFact` sparingly for important durable facts.
**Session end:** Write a handover entry to the log:
  - Key decisions made
  - Pending items
  - Next steps

## Retrieval Discipline
Before creating anything, check if it already exists. Before assuming a convention,
check rules.md. Before asking the user, try to find the answer in your memory.

## Response Guidelines
- Factual questions: answer directly, cite uncertainty
- Tasks: confirm → do → report
- Vague requests: ask ONE clarifying question
- Emotional topics: acknowledge first, then help

## Proactivity
If you notice a pattern in what the user asks, mention it naturally.
Offer to save it to memory if it seems recurring.
```

---

### Improvement 2 — Give the Agent Hands (Tools + MCP)

**Problem:** `tools: null` in Program.cs. The agent can only talk. It can't read files, execute commands, fetch URLs, or interact with external tools. It also can't write to its own memory files without the tools from Improvement 1.

**Solution:** Register built-in tools via `AIFunctionFactory.Create()` (MAF pattern), load MCP tools at startup via the official C# MCP SDK, and merge everything into the tool list passed to `AsAIAgent()`.

#### Why this is simpler than msclaw/OpenClaw/nanobot

- **msclaw** has no tools at all — it's chat-only like us currently.
- **OpenClaw** has a plugin SDK with npm registry, ClawHub marketplace, Canvas/A2UI — massive infrastructure we don't need.
- **nanobot** has the right idea (plain functions as tools) but its MCP bridge is custom Python code.

We lean on two things that already exist in our stack:
1. **MAF's `AIFunctionFactory.Create()`** — wraps any C# method into an `AITool` that the agent loop auto-invokes. Zero boilerplate.
2. **Official `ModelContextProtocol` C# SDK** — connect to any MCP server (stdio or HTTP), list its tools, call them. The SDK does the JSON-RPC plumbing.

#### Architecture

```
Program.cs startup
    │
    ├── Built-in tools (C# methods → AITool via AIFunctionFactory)
    │     ├── MemoryTool.AppendLogAsync        ← from Improvement 1
    │     ├── MemoryTool.AddRuleAsync           
    │     ├── MemoryTool.SaveFactAsync          
    │     └── ExecTool.RunAsync                ← shell execution (covers file I/O + HTTP via curl)
    │
    ├── MCP tools (loaded at startup from config)
    │     ├── config: Mcp:Servers[] → stdio/http transport
    │     ├── ModelContextProtocol SDK connects to each server
    │     ├── ListToolsAsync() discovers available tools
    │     └── Each MCP tool wrapped as AITool
    │
    └── All tools merged → List<AITool>
          │
          ▼
    copilotClient.AsAIAgent(..., tools: mergedTools, instructions: systemMessage)
          │
          ▼
    MAF agent loop: agent decides when to call tools
    GitHub Copilot SDK: provides the LLM backbone
    MCP servers: extend capabilities without code changes
```

#### Built-in tools

```csharp
// ── ExecTool.cs (new file) ─────────────────────────────────────────────
using System.ComponentModel;
using System.Diagnostics;

namespace DotNetClaw;

public sealed class ExecTool
{
    private static readonly string[] Blocked =
        ["rm -rf /", "mkfs", "dd if=", ":(){ :|:& };:", "shutdown", "reboot",
         "format c:", "del /f /s /q"];

    [Description("Execute a shell command. Returns stdout + stderr + exit code. " +
                 "Use for: git, dotnet, npm, curl, system info. " +
                 "Dangerous commands (rm -rf /, shutdown, etc.) are blocked.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
        if (Blocked.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "Blocked by safety filter.";

        var isWindows = OperatingSystem.IsWindows();
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        proc.Start();

        // Read both streams concurrently to avoid deadlocks
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        await proc.WaitForExitAsync(ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        var result = $"exit={proc.ExitCode}\n{stdout}";
        if (!string.IsNullOrWhiteSpace(stderr))
            result += $"\nSTDERR:\n{stderr}";

        return result.Length > 8000 ? result[..8000] + "\n[truncated]" : result;
    }
}
```

> **YAGNI note:** `FileSystemTool` and `WebTool` are not needed — `ExecTool.RunAsync`
> can do `cat`, `ls`, `curl` etc. via shell commands. Two fewer files, same capability.

#### MCP tool loading

```csharp
// ── McpToolLoader.cs (new file) ────────────────────────────────────────
// NuGet: ModelContextProtocol (official C# MCP SDK)
//
// This is simpler than nanobot's custom Python MCP bridge and infinitely
// simpler than OpenClaw's mcporter. The official SDK does the heavy lifting.

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol.Types;
using Microsoft.Agents.AI;

namespace DotNetClaw;

public static class McpToolLoader
{
    /// <summary>
    /// Connect to MCP servers defined in config and wrap their tools as MAF AITools.
    /// Each MCP tool becomes a callable function in the agent's tool belt.
    /// </summary>
    public static async Task<List<AITool>> LoadAsync(
        IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        var tools = new List<AITool>();
        var servers = config.GetSection("Mcp:Servers").GetChildren();

        foreach (var server in servers)
        {
            var name = server["Name"] ?? "unnamed";
            var transport = server["Transport"] ?? "stdio";

            try
            {
                IMcpClient client = transport switch
                {
                    "stdio" => await McpClientFactory.CreateAsync(
                        new McpClientOptions { ClientInfo = new() { Name = "dotnetclaw" } },
                        new StdioClientTransportOptions
                        {
                            Command = server["Command"]!,
                            Arguments = server.GetSection("Args").Get<string[]>() ?? [],
                        }, ct),

                    "http" => await McpClientFactory.CreateAsync(
                        new McpClientOptions { ClientInfo = new() { Name = "dotnetclaw" } },
                        new SseClientTransportOptions
                        {
                            Endpoint = new Uri(server["Url"]!),
                        }, ct),

                    _ => throw new ArgumentException($"Unknown transport: {transport}")
                };

                var mcpTools = await client.ListToolsAsync(ct);
                foreach (var mcpTool in mcpTools)
                {
                    // Wrap each MCP tool as a MAF AITool
                    // The lambda captures client + tool name for invocation
                    var toolName = mcpTool.Name;
                    var toolDesc = mcpTool.Description ?? toolName;

                    tools.Add(AIFunctionFactory.Create(
                        async (string input, CancellationToken c) =>
                        {
                            var result = await client.CallToolAsync(
                                toolName,
                                new Dictionary<string, object?> { ["input"] = input }, c);
                            return result.Content.FirstOrDefault()?.Text ?? "";
                        },
                        toolName,
                        toolDesc));

                    logger.LogInformation("[MCP] Registered tool: {Server}/{Tool}", name, toolName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[MCP] Failed to connect to server: {Name}", name);
                // Non-fatal — other servers and built-in tools still work
            }
        }

        return tools;
    }
}
```

#### Wiring it all together in Program.cs

```csharp
// ── Program.cs — updated AIAgent registration ──────────────────────────

builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind = sp.GetRequiredService<MindLoader>();
    var systemMessage = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    var logger = sp.GetRequiredService<ILogger<ClawRuntime>>();

    // ── Built-in tools ──────────────────────────────────────────────
    var memTool  = new MemoryTool(mind.MindRoot);
    var execTool = new ExecTool();

    var tools = new List<AITool>
    {
        // Memory (Improvement 1)
        AIFunctionFactory.Create(memTool.AppendLogAsync),
        AIFunctionFactory.Create(memTool.AddRuleAsync),
        AIFunctionFactory.Create(memTool.SaveFactAsync),

        // World interaction (covers file I/O + HTTP via shell)
        AIFunctionFactory.Create(execTool.RunAsync),
    };

    // ── MCP tools (load from config, non-fatal if none configured) ──
    var mcpTools = McpToolLoader.LoadAsync(
        sp.GetRequiredService<IConfiguration>(), logger, default)
        .GetAwaiter().GetResult();
    tools.AddRange(mcpTools);

    logger.LogInformation("Agent initialized with {Count} tools ({BuiltIn} built-in, {Mcp} MCP)",
        tools.Count, tools.Count - mcpTools.Count, mcpTools.Count);

    // ── Create agent ────────────────────────────────────────────────
    var copilotClient = new CopilotClient(new CopilotClientOptions
    {
        Cwd = mind.MindRoot,
        AutoStart = true,
        UseStdio = true,
    });

    return copilotClient.AsAIAgent(
        ownsClient:   true,
        id:           "dotnetclaw",
        name:         "DotNetClaw",
        description:  "Personal AI assistant with tools and memory",
        tools:        tools,          // ← was null
        instructions: systemMessage);
});
```

---

### Improvement 3 — Channel Coverage (Reach Users Everywhere)

**Problem:** DotNetClaw currently speaks only Slack. OpenClaw covers 22+ platforms; nanobot covers 10. For a personal assistant, being reachable on the platforms you *actually use* matters more than having the smartest brain.

**Solution:** Add Microsoft Teams (Bot Framework), Telegram, and Discord using the same proven pattern as `SlackChannel.cs`. Each channel is a self-contained ~80-line adapter — zero coupling between channels.

#### Why this is simpler than OpenClaw/nanobot

- **OpenClaw** abstracts channels behind a Gateway + adapter registry with webhook routing, OAuth per-platform, and a control plane. Overkill for us.
- **nanobot** uses `BaseChannel` inheritance with `send()`, `receive()`, `format()` overrides. Clean, but we don't need an ABC — our channels are just extension methods that register endpoints/listeners.
- **Our pattern** (already proven in `SlackChannel.cs`): each channel is a static class with `AddXxxChannel()` (DI) + `MapXxxChannel()` (endpoints) or a `BackgroundService` (for WebSocket-based platforms). All channels converge on `runtime.HandleAsync(sessionId, text, ct)`.

We add **Teams**, **Telegram**, and **Discord** because they're the three most-used messaging platforms alongside Slack.

#### Architecture

```
            All 4 channels are independent, self-contained adapters
            Each converges on the same runtime entry point

   ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
   │   TeamsChannel    │  │   SlackChannel    │  │ TelegramChannel  │  │  DiscordChannel  │
   │  Bot Framework    │  │   Socket Mode     │  │ Polling loop     │  │  WebSocket GW    │
   │  POST /api/       │  │   (SlackNet)      │  │ BackgroundService│  │  BackgroundService│
   │  messages         │  │                   │  │                  │  │                  │
   │                   │  │  DM: message.im   │  │                  │  │  DM + channel    │
   │  session:         │  │  @mention:        │  │  session:         │  │  session:         │
   │  "teams:{convId}" │  │  app_mention      │  │  "telegram:{id}" │  │  "discord:dm:{id}"│
   │                   │  │                   │  │                  │  │  "discord:{c}:{u}"│
   │  max: 28000 chars │  │  session:          │  │  max: 4096 chars │  │  max: 2000 chars │
   │                   │  │  "slack:dm:{uid}" │  │                  │  │                  │
   │  NuGet:           │  │  "slack:{ch}:{u}" │  │  NuGet:          │  │  NuGet:          │
   │  Microsoft.Bot.   │  │                   │  │  Telegram.Bot    │  │  Discord.Net     │
   │  Builder          │  │  max: 4000 chars  │  │                  │  │                  │
   │  🆕 NEW           │  │  ✅ EXISTS         │  │  🆕 NEW          │  │  🆕 NEW          │
   └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘
            │                      │                      │                      │
            └──────────────────────┴──────────────────────┴──────────────────────┘
                                            │
                              runtime.HandleAsync(sessionId, text, ct)
```

#### Channel summary

| Channel | Transport | Session ID | Max chars | NuGet | Status |
|---|---|---|---|---|---|
| Teams | Bot Framework webhook (POST) | `teams:{conversationId}` | 28000 | Microsoft.Bot.Builder, Microsoft.Bot.Builder.Integration.AspNet.Core | 🆕 New |
| Slack | SlackNet Socket Mode (WebSocket) | `slack:dm:{userId}` / `slack:{channelId}:{userId}` | 4000 | SlackNet | ✅ Exists |
| Telegram | Long-polling (`GetUpdates`) | `telegram:{chatId}` | 4096 | Telegram.Bot | 🆕 New |
| Discord | Discord Gateway (WebSocket) | `discord:dm:{userId}` / `discord:{channelId}:{userId}` | 2000 | Discord.Net | 🆕 New |

#### Channel convention (no interface needed)

All channels follow the same pattern by convention — no shared interface required:
1. `AddXxxChannel(services, config)` — register DI
2. `MapXxxChannel(app)` — map endpoints or start listeners (webhook channels only)
3. All messages converge on `runtime.HandleAsync(sessionId, text, ct)`

This is deliberate: channels have different base classes (`ActivityHandler` for Teams, `BackgroundService` for Telegram/Discord, static class for Slack), so a shared interface would be empty ceremony. The convention is the contract.

#### TeamsChannel (new)

```csharp
// ── TeamsChannel.cs (new file) ─────────────────────────────────────────
// Transport: Bot Framework webhook (JSON POST)
// Auth: Bot Framework authentication (JWT bearer validation)
// Session: conversationId → "teams:{conversationId}"

using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;

namespace DotNetClaw;

public class ClawBot : ActivityHandler
{
    private readonly ClawRuntime _runtime;
    private readonly ILogger<ClawBot> _logger;

    public ClawBot(ClawRuntime runtime, ILogger<ClawBot> logger)
    {
        _runtime = runtime;
        _logger = logger;
    }

    protected override async Task OnMessageActivityAsync(
        ITurnContext<IMessageActivity> turnContext, CancellationToken ct)
    {
        var text = turnContext.Activity.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        var sessionId = $"teams:{turnContext.Activity.Conversation.Id}";
        _logger.LogInformation("[Teams] {Session}: {Text}", sessionId, text);

        var reply = await _runtime.HandleAsync(sessionId, text, ct);

        if (!string.IsNullOrWhiteSpace(reply))
        {
            if (reply.Length > 28_000) reply = reply[..27_990] + " [...]";
            await turnContext.SendActivityAsync(MessageFactory.Text(reply), ct);
        }
    }
}

public static class TeamsChannelExtensions
{
    public static IServiceCollection AddTeamsChannel(this IServiceCollection services)
    {
        services.AddSingleton<IBotFrameworkHttpAdapter, CloudAdapter>();
        services.AddTransient<IBot, ClawBot>();
        return services;
    }

    public static IEndpointRouteBuilder MapTeams(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/messages", async (HttpContext context,
            IBotFrameworkHttpAdapter adapter, IBot bot) =>
        {
            await adapter.ProcessAsync(context.Request, context.Response, bot);
        });
        return app;
    }
}
```

#### SlackChannel (existing — no changes needed)

Already implemented in `SlackChannel.cs` (163 lines). Handles `MessageEvent` (DMs)
and `AppMention` (@mentions) via SlackNet Socket Mode. Session IDs: `slack:dm:{userId}`
and `slack:{channelId}:{userId}`. Max message length: 4000 chars.

See [SlackChannel.cs](SlackChannel.cs) for the full implementation.

#### TelegramChannel (new)

```csharp
// ── TelegramChannel.cs (new file) ──────────────────────────────────────
// NuGet: Telegram.Bot
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace DotNetClaw;

public sealed class TelegramChannel(ClawRuntime runtime, IConfiguration config, ILogger<TelegramChannel> logger)
    : BackgroundService
{
    private readonly TelegramBotClient _bot = new(config["Telegram:BotToken"]!);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("[Telegram] Starting polling...");
        var offset = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var updates = await _bot.GetUpdates(offset, timeout: 30, cancellationToken: ct);
                foreach (var u in updates)
                {
                    if (u.Message?.Text is { } text)
                    {
                        var chatId = u.Message.Chat.Id;
                        var sessionId = $"telegram:{chatId}";

                        logger.LogDebug("[Telegram] [{Session}] → {Preview}",
                            sessionId, text[..Math.Min(80, text.Length)]);

                        var reply = await runtime.HandleAsync(sessionId, text, ct);
                        if (!string.IsNullOrWhiteSpace(reply))
                        {
                            // Telegram max message: 4096 chars
                            if (reply.Length > 4096)
                                reply = reply[..4093] + "...";
                            await _bot.SendMessage(chatId, reply, cancellationToken: ct);
                        }
                    }
                    offset = u.Id + 1;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Telegram] Polling error, retrying in 5s...");
                await Task.Delay(5000, ct);
            }
        }
    }
}

public static class TelegramExtensions
{
    public static IServiceCollection AddTelegramChannel(this IServiceCollection services, IConfiguration config)
    {
        if (!string.IsNullOrEmpty(config["Telegram:BotToken"]))
            services.AddHostedService<TelegramChannel>();
        return services;
    }
}
```

#### DiscordChannel (new)

```csharp
// ── DiscordChannel.cs (new file) ───────────────────────────────────────
// NuGet: Discord.Net
using Discord;
using Discord.WebSocket;

namespace DotNetClaw;

public sealed class DiscordChannel : BackgroundService
{
    private readonly ClawRuntime _runtime;
    private readonly DiscordSocketClient _client;
    private readonly string _token;
    private readonly ILogger<DiscordChannel> _logger;

    public DiscordChannel(ClawRuntime runtime, IConfiguration config, ILogger<DiscordChannel> logger)
    {
        _runtime = runtime;
        _token = config["Discord:BotToken"]!;
        _logger = logger;
        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.MessageContent
        });
        _client.MessageReceived += OnMessageReceivedAsync;
    }

    private async Task OnMessageReceivedAsync(SocketMessage msg)
    {
        if (msg.Author.IsBot || msg is not SocketUserMessage userMsg) return;

        var sessionId = msg.Channel is IDMChannel
            ? $"discord:dm:{msg.Author.Id}"
            : $"discord:{msg.Channel.Id}:{msg.Author.Id}";

        _logger.LogDebug("[Discord] [{Session}] → {Preview}",
            sessionId, msg.Content[..Math.Min(80, msg.Content.Length)]);

        var reply = await _runtime.HandleAsync(sessionId, msg.Content, default);
        if (!string.IsNullOrWhiteSpace(reply))
        {
            // Discord max message: 2000 chars
            if (reply.Length > 2000)
                reply = reply[..1997] + "...";
            await msg.Channel.SendMessageAsync(reply);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Discord] Connecting...");
        await _client.LoginAsync(TokenType.Bot, _token);
        await _client.StartAsync();
        await Task.Delay(Timeout.Infinite, ct);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        await _client.StopAsync();
        await base.StopAsync(ct);
    }
}

public static class DiscordExtensions
{
    public static IServiceCollection AddDiscordChannel(this IServiceCollection services, IConfiguration config)
    {
        if (!string.IsNullOrEmpty(config["Discord:BotToken"]))
            services.AddHostedService<DiscordChannel>();
        return services;
    }
}
```

#### Wiring in Program.cs

```csharp
// ── Program.cs — all 4 channels ────────────────────────────────────────
// Existing:
builder.Services.AddTeamsChannel();                              // Teams (Bot Framework)
builder.Services.AddSlackChannel(builder.Configuration);         // Slack

// New:
builder.Services.AddTelegramChannel(builder.Configuration);      // Telegram (no-op if token empty)
builder.Services.AddDiscordChannel(builder.Configuration);       // Discord  (no-op if token empty)

// ...
app.MapTeams();      // POST /api/messages
app.MapSlack(builder.Configuration);  // Socket Mode listener
// Telegram + Discord start as BackgroundServices — no endpoint mapping needed
```

#### Config additions

```jsonc
// appsettings.json — full channel config (Teams/Slack already exist)
{
  "Teams": {
    "MicrosoftAppId": "",        // dotnet user-secrets (from Azure Bot registration)
    "MicrosoftAppPassword": "",  // dotnet user-secrets (client secret)
    "MicrosoftAppTenantId": ""   // dotnet user-secrets (optional, for single-tenant)
  },
  "Slack": {
    "BotToken": "",       // dotnet user-secrets (xoxb-...)
    "AppToken": "",       // dotnet user-secrets (xapp-...)
    "BotUserId": "",      // dotnet user-secrets
    "Policy": "open",
    "AllowedUserIds": ""
  },
  "Telegram": {
    "BotToken": ""        // from @BotFather, dotnet user-secrets in production
  },
  "Discord": {
    "BotToken": ""        // from Discord Developer Portal, dotnet user-secrets in production
  }
}
```

**Key design choice:** Channels auto-disable when their token is empty. No feature flags needed — if `Telegram:BotToken` is blank, `AddTelegramChannel` is a no-op. This means you can deploy with just Teams + Slack and add Telegram/Discord later by simply setting secrets.

---

### Improvement 4 — Make the Runtime Smart (Commands + Persistence + UX)

**Problem:** `ClawRuntime.HandleAsync()` is a passthrough — it just forwards to the agent. No chat commands, no session persistence, no typing indicators, no health diagnostics.

**Solution:** Add a thin command layer, session awareness, and quality-of-life features.

#### Why this is better than the gaps.md approach

gaps.md proposed `SessionStore.cs` with JSONL persistence, separate `PairingService`, `SessionRateLimiter`, `UsageTracker`, `CronService`, `WebhookEndpoints`, `DoctorEndpoints` — 7 new files for features we don't need yet. We take only what matters now:

1. **Chat commands** (`/reset`, `/status`, `/help`) — 20 lines in `ClawRuntime.cs`
2. **Typing indicators** — 5 lines in `SlackChannel.cs`
3. **Health endpoint** — 10 lines in `Program.cs`

Session persistence via JSONL is deferred — MAF's `AgentSession` doesn't expose its conversation history as a serializable list (it's an opaque object managed by the Copilot SDK bridge). When MAF adds session export/import, we'll add persistence. Premature abstraction hurts more than volatile sessions.

#### ClawRuntime changes

```csharp
// ── ClawRuntime.cs — add command interception ──────────────────────────

public sealed class ClawRuntime(AIAgent agent, ILogger<ClawRuntime> logger)
{
    private readonly ConcurrentDictionary<string, AgentSession> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly ConcurrentDictionary<string, DateTime> _created = new();
    private long _totalRequests;

    public async Task<string> HandleAsync(
        string sessionId, string message, CancellationToken ct = default)
    {
        // ── Chat commands (OpenClaw/nanobot pattern) ────────────────
        if (message.StartsWith('/'))
        {
            return message.Split(' ', 2)[0].ToLowerInvariant() switch
            {
                "/reset"  => ResetSession(sessionId),
                "/status" => GetStatus(sessionId),
                "/help"   => "Commands:\n/reset — clear conversation\n/status — session info\n/help — this message",
                _         => await RunAgentAsync(sessionId, message, ct)
            };
        }

        return await RunAgentAsync(sessionId, message, ct);
    }

    private string ResetSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _locks.TryRemove(sessionId, out _);
        _created.TryRemove(sessionId, out _);
        logger.LogInformation("[{Session}] Reset", sessionId);
        return "Session cleared. Starting fresh.";
    }

    private string GetStatus(string sessionId)
    {
        var active = _sessions.ContainsKey(sessionId);
        var created = _created.TryGetValue(sessionId, out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm") : "never";
        return $"Session: {sessionId}\nActive: {active}\nCreated: {created}\nTotal requests: {_totalRequests}";
    }

    private async Task<string> RunAgentAsync(
        string sessionId, string message, CancellationToken ct)
    {
        Interlocked.Increment(ref _totalRequests);
        var session = await GetOrCreateAsync(sessionId, ct);
        var sem = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(ct);
        try
        {
            logger.LogDebug("[{Session}] → {Preview}",
                sessionId, message[..Math.Min(80, message.Length)]);

            string result = string.Empty;
            await foreach (var update in agent.RunStreamingAsync(message, session, null, ct))
            {
                if (!string.IsNullOrEmpty(update.Text))
                    result = update.Text;
            }

            logger.LogDebug("[{Session}] ← {Len} chars", sessionId, result.Length);
            return result;
        }
        finally
        {
            sem.Release();
        }
    }

    private async ValueTask<AgentSession> GetOrCreateAsync(
        string sessionId, CancellationToken ct)
    {
        if (_sessions.TryGetValue(sessionId, out var existing)) return existing;
        var newSession = await agent.CreateSessionAsync(ct);
        _sessions.TryAdd(sessionId, newSession);
        _created.TryAdd(sessionId, DateTime.UtcNow);
        return newSession;
    }

    // For health endpoint
    public object GetHealthReport() => new
    {
        TotalRequests = _totalRequests,
        ActiveSessions = _sessions.Count,
        Uptime = DateTime.UtcNow - _startedAt,
    };
    private readonly DateTime _startedAt = DateTime.UtcNow;
}
```

#### Slack typing indicator

Add a `thinking_face` reaction before calling the agent and remove it afterward.
Wrap each `Handle` method body in try/finally:

```csharp
// In both Handle(MessageEvent e) and Handle(AppMention e):
try { await slack.Reactions.AddReaction(e.Channel, e.Ts, "thinking_face"); }
catch { /* ignore */ }

try
{
    // ... existing handler body (HandleAsync + PostMessage) ...
}
finally
{
    try { await slack.Reactions.RemoveReaction(e.Channel, e.Ts, "thinking_face"); }
    catch { /* ignore — message may have been deleted */ }
}
```

#### Health endpoint

```csharp
// ── Program.cs — add health endpoint ───────────────────────────────────

app.MapGet("/health", (ClawRuntime runtime, MindLoader mind) =>
{
    var health = runtime.GetHealthReport();
    return Results.Ok(new
    {
        Name = "DotNetClaw",
        Status = "running",
        Mind = mind.MindRoot,
        health,
    });
});
```

---

## Implementation Workflow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    IMPROVEMENT 1: WIRE THE MIND                     │
│                                                                     │
│  Step 1: Add rules.md + log.md to mind/.working-memory/            │
│  Step 2: Update MindLoader to read all 3 files into system message │
│  Step 3: Create MemoryTool.cs (3 tool methods)                     │
│  Step 4: Update assistant.agent.md with memory protocol            │
│                                                                     │
│  Files changed: MindLoader.cs, assistant.agent.md                  │
│  Files added:   MemoryTool.cs, rules.md, log.md                   │
│  LOC delta:     ~120 lines                                         │
└──────────────────────────────────┬──────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                  IMPROVEMENT 2: GIVE THE AGENT HANDS                │
│                                                                     │
│  Step 5: Create ExecTool.cs                                        │
│  Step 6: Create McpToolLoader.cs                                   │
│  Step 7: Update Program.cs — register all tools, pass to AsAIAgent │
│  Step 8: Add Mcp:Servers config section to appsettings.json        │
│                                                                     │
│  Files changed: Program.cs, DotNetClaw.csproj, appsettings.json   │
│  Files added:   ExecTool.cs, McpToolLoader.cs                      │
│  NuGet added:   ModelContextProtocol                               │
│  LOC delta:     ~120 lines                                         │
└──────────────────────────────────┬──────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                IMPROVEMENT 3: CHANNEL COVERAGE                      │
│                                                                     │
│  Step 9:  Create TeamsChannel.cs (Bot Framework webhook)           │
│  Step 10: Create TelegramChannel.cs (polling BackgroundService)    │
│  Step 11: Create DiscordChannel.cs (WebSocket BackgroundService)   │
│  Step 12: Update Program.cs — register new channels                │
│  Step 13: Add Teams/Telegram/Discord config to appsettings.json    │
│                                                                     │
│  Files added:   TeamsChannel.cs, TelegramChannel.cs,               │
│                 DiscordChannel.cs                                   │
│  Files changed: Program.cs, DotNetClaw.csproj, appsettings.json   │
│  NuGet added:   Microsoft.Bot.Builder, Telegram.Bot, Discord.Net   │
│  LOC delta:     ~160 lines                                         │
└──────────────────────────────────┬──────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                IMPROVEMENT 4: MAKE THE RUNTIME SMART                │
│                                                                     │
│  Step 14: Add chat commands to ClawRuntime.cs                      │
│  Step 15: Add typing indicator to SlackChannel.cs                  │
│  Step 16: Add /health endpoint to Program.cs                       │
│                                                                     │
│  Files changed: ClawRuntime.cs, SlackChannel.cs, Program.cs       │
│  LOC delta:     ~60 lines                                          │
└─────────────────────────────────────────────────────────────────────┘

Total: ~460 lines of new/changed code
       6 new files (MemoryTool.cs, ExecTool.cs, McpToolLoader.cs,
                    TeamsChannel.cs, TelegramChannel.cs, DiscordChannel.cs)
       3 changed files (MindLoader.cs, ClawRuntime.cs, Program.cs)
       2 changed configs (appsettings.json, assistant.agent.md)
       2 new mind files (rules.md, log.md)
```

---

## Before vs. After

| Capability | Before | After |
|---|---|---|
| Memory | Agent told to read memory but never sees it | 3-file memory injected into every system message |
| Memory persistence | memory.md exists but agent can't write to it | Agent has 3 memory tools (log, rule, fact) |
| Tools | `tools: null` | 4 built-in tools + unlimited MCP tools |
| Shell execution | Not possible | `ExecTool.RunAsync` with safety filter (also covers file I/O + HTTP) |
| File I/O | Not possible | Via `ExecTool` — `cat`, `ls`, etc. |
| HTTP fetch | Not possible | Via `ExecTool` — `curl` |
| MCP | Not possible | Connect to any MCP server (stdio/HTTP) at startup |
| Channels | 1 (Slack) | 4 (Teams, Slack, Telegram, Discord) — auto-disable when token empty |
| Chat commands | Not possible | `/reset`, `/status`, `/help` |
| Typing indicator | None | Slack `thinking_face` reaction during processing |
| Health check | `{ name, status }` only | `{ name, status, uptime, sessions, requests, mind }` |
| Session handover | None | Agent instructed to write log entry at session end |
| Rule learning | None | Agent can `AddRule` when it makes mistakes |
| Log consolidation | None | Agent can consolidate log → memory when log grows |

---

## What We Explicitly Defer (and Why)

| Feature | Why defer |
|---|---|
| Session persistence (JSONL) | MAF's `AgentSession` is opaque — no serialize/deserialize API yet. When MAF adds it, we add persistence. |
| Multi-provider LLM | Copilot SDK is our strategic bet. Adding OpenAI/Anthropic adds complexity without clear value for a personal bot. |

| Cron / automation | Premature — the agent can't even act yet. Add after tools are proven. |
| Docker sandbox | Overkill for personal bot. The safety filter + workspace restriction is enough. |
| msclaw's bootstrap wizard | The agent already has a personality. If users want to customize, they edit SOUL.md directly. |

---

## Key Insight: Why This Works Better Than msclaw

msclaw's bootstrap approach is **infrastructure-first**: build 5 C# classes, 10 directories, embedded templates, and a 3-phase interview wizard before the agent can do anything. The agent's identity is defined through an elaborate ceremony.

Our approach is **capability-first**: the agent already has identity (SOUL.md exists, it's good). What it lacks is **capability** (can't act) and **continuity** (can't remember). We add those directly:

1. **Continuity** = inject working memory into system message + give agent write tools (Improvement 1)
2. **Capability** = register C# tools + load MCP tools (Improvement 2)
3. **Reach** = Teams + Telegram + Discord channels so the agent meets you where you are (Improvement 3)
4. **Quality** = chat commands + typing indicators + health (Improvement 4)

The agent becomes useful after ~540 lines of changes. msclaw's approach would require ~500+ lines just for the Mind infrastructure, before the agent gains any new capability.

We keep our harness thin because **GitHub Copilot SDK** provides the LLM backbone and **MAF** provides the agent loop + tool calling. We don't rebuild what they give us for free. We just wire the pieces together and add the memory layer that makes the agent feel alive.

---

## Implementation Checklist

### Improvement 1 — Wire the Mind

- [ ] **1.1 Create `mind/.working-memory/rules.md`**
  > **Test:** `cat mind/.working-memory/rules.md` → file exists with `# Rules` header.

- [ ] **1.2 Create `mind/.working-memory/log.md`**
  > **Test:** `cat mind/.working-memory/log.md` → file exists with `# Log` header.

- [ ] **1.3 Update `MindLoader.cs` to read all 3 working-memory files into system message**
  > **Test — unit:** Call `LoadSystemMessageAsync()` and assert the returned string contains content from `memory.md`, `rules.md`, and `log.md`. Assert the "## Working Memory" header is present.
  >
  > **Test — boundary:** Delete `rules.md` → call `LoadSystemMessageAsync()` → no exception, system message still includes `memory.md` and `log.md`.
  >
  > **Test — log trimming:** Add 100 lines to `log.md` → call `LoadSystemMessageAsync()` → assert only the last 50 lines appear in the returned string.

- [ ] **1.4 Create `MemoryTool.cs` with 3 methods (`AppendLog`, `AddRule`, `SaveFact`)**
  > **Test — `AppendLogAsync`:** Call it with entry `"test log"` → read `log.md` → last line matches `- [yyyy-MM-dd HH:mm] test log`.
  >
  > **Test — `AddRuleAsync`:** Call it with `"Always check file exists"` → read `rules.md` → contains `- Always check file exists`.
  >
  > **Test — `SaveFactAsync`:** Call it with `"User prefers dark mode"` → read `memory.md` → contains `- [yyyy-MM-dd] User prefers dark mode`.
  >
  > **Test — directory creation:** Delete `.working-memory/` → call `AppendLogAsync` → directory and file are recreated automatically.

- [ ] **1.5 Update `assistant.agent.md` with memory protocol instructions**
  > **Test:** Read the assembled system message → contains "Memory Protocol" section with the 3-file table and session lifecycle instructions.

- [ ] **1.6 End-to-end: agent uses memory tools during conversation**
  > **Test — integration:** Send a Teams/Slack message "Remember that my favorite color is blue" → agent calls `SaveFactAsync` → `/reset` → send "What's my favorite color?" → agent responds with "blue" (because memory.md was reloaded into system message).

---

### Improvement 2 — Give the Agent Hands

- [ ] **2.1 Create `ExecTool.cs` with `RunAsync`**
  > **Test — happy path:** Call `RunAsync("echo hello")` → returns `exit=0\nhello`.
  >
  > **Test — safety filter:** Call `RunAsync("rm -rf /")` → returns "Blocked by safety filter." without executing.
  >
  > **Test — stderr:** Call `RunAsync("ls /nonexistent")` → returns non-zero exit code with STDERR content.
  >
  > **Test — truncation:** Call `RunAsync("cat /dev/urandom | head -c 20000 | base64")` → output truncated to 8000 chars with `[truncated]` suffix.

- [ ] **2.2 Create `McpToolLoader.cs`**
  > **Test — no servers configured:** `LoadAsync()` with empty `Mcp:Servers` config → returns empty list, no errors.
  >
  > **Test — stdio server:** Configure a test MCP server (e.g., `npx @modelcontextprotocol/server-everything`) → `LoadAsync()` returns `List<AITool>` with tools from that server → logger shows `[MCP] Registered tool: ...` entries.
  >
  > **Test — unreachable server:** Configure a server with invalid command → `LoadAsync()` logs warning but returns tools from other working servers (non-fatal).
  >
  > **Test — tool invocation:** Load a real MCP tool → call it through MAF agent loop → verify it returns a result (integration test via Teams/Slack).

- [ ] **2.3 Add `ModelContextProtocol` NuGet package to `DotNetClaw.csproj`**
  > **Test:** `dotnet restore` succeeds. `dotnet build` succeeds with no missing reference errors.

- [ ] **2.4 Update `Program.cs` to register all tools and pass to `AsAIAgent`**
  > **Test — startup:** `dotnet run` → logs `Agent initialized with N tools (X built-in, Y MCP)`.
  >
  > **Test — tools non-null:** Set a breakpoint or add a log after `AsAIAgent()` → verify `tools` list has ≥ 4 items.

- [ ] **2.5 Add `Mcp:Servers` config section to `appsettings.json`**
  > **Test:** `appsettings.json` is valid JSON after edit. App starts with the new section (even if empty array).

- [ ] **2.6 End-to-end: agent executes a tool during conversation**
  > **Test:** Send Slack message "Run `echo hello world` in the shell" → agent calls `ExecTool.RunAsync` → replies with `exit=0\nhello world`.
  >
  > **Test:** Send "List the files in the mind directory" → agent calls `ExecTool.RunAsync("ls ./mind")` → replies with directory listing.

---

### Improvement 3 — Channel Coverage

- [ ] **3.1 Create `TeamsChannel.cs` using Bot Framework**
  > **Test — webhook roundtrip:** `curl -X POST http://localhost:5000/api/messages -H "Content-Type: application/json" -d '{"type":"message","text":"hello","conversation":{"id":"test-conv"}}'` → Bot Framework auth rejects (expected, proves endpoint is wired). With a valid Bot Framework token → agent processes and replies.
  >
  > **Test — session ID format:** Send a Teams message → logs show session ID as `teams:{conversationId}`.
  >
  > **Test — message truncation:** Response > 28000 chars → truncated to 27990 chars with ` [...]` suffix.
  >
  > **Test — empty text ignored:** Send an activity with no text → handler returns without calling agent.

- [ ] **3.2 Verify `SlackChannel.cs` follows channel convention**
  > **Test — DM roundtrip:** Send a DM to the Slack bot → bot replies in-thread. Session ID format is `slack:dm:{userId}`.
  >
  > **Test — @mention roundtrip:** @mention the bot in a channel → bot replies in-thread. Session ID format is `slack:{channelId}:{userId}`.
  >
  > **Test — bot self-filter:** Bot's own messages don't trigger re-processing (no infinite loop).
  >
  > **Test — access policy (open):** Set `Slack:Policy` to `"open"` → any user can DM the bot.
  >
  > **Test — access policy (allowlist):** Set `Slack:Policy` to `"allowlist"` + `Slack:AllowedUserIds` to a specific ID → only that user gets responses, others are blocked with log message.
  >
  > **Test — message truncation:** Response > 4000 chars → truncated to 3990 chars with `\n[...]` suffix.
  >
  > **Test — socket mode:** App starts without public URL. Logs show SlackNet Socket Mode connection established.

- [ ] **3.3 Create `TelegramChannel.cs` (polling BackgroundService)**
  > **Test — startup without token:** Leave `Telegram:BotToken` empty → app starts normally, no Telegram service registered, no errors in logs.
  >
  > **Test — startup with token:** Set `Telegram:BotToken` via `dotnet user-secrets set "Telegram:BotToken" "<token>"` → app starts → logs `[Telegram] Starting polling...`.
  >
  > **Test — message roundtrip:** Send a DM to the Telegram bot → bot replies with agent response. Session ID format is `telegram:{chatId}`.
  >
  > **Test — message truncation:** Send a prompt that generates a response > 4096 chars → response is truncated to 4096 with `...` suffix.
  >
  > **Test — polling resilience:** Kill network briefly → bot logs warning `[Telegram] Polling error, retrying in 5s...` → reconnects automatically.

- [ ] **3.4 Create `DiscordChannel.cs` (WebSocket BackgroundService)**
  > **Test — startup without token:** Leave `Discord:BotToken` empty → app starts normally, no Discord service registered.
  >
  > **Test — startup with token:** Set `Discord:BotToken` → app starts → logs `[Discord] Connecting...`.
  >
  > **Test — DM roundtrip:** Send a DM to the Discord bot → bot replies. Session ID format is `discord:dm:{userId}`.
  >
  > **Test — channel message:** @mention or message the bot in a channel → bot replies. Session ID format is `discord:{channelId}:{userId}`.
  >
  > **Test — message truncation:** Response > 2000 chars → truncated to 2000 with `...` suffix.
  >
  > **Test — bot ignores itself:** Bot's own messages don't trigger re-processing (no infinite loop).

- [ ] **3.5 Add `Microsoft.Bot.Builder`, `Telegram.Bot` and `Discord.Net` NuGet packages to `DotNetClaw.csproj`**
  > **Test:** `dotnet restore && dotnet build` succeeds with no missing reference errors.

- [ ] **3.6 Register all channels in `Program.cs`**
  > **Test:** All 4 channel registrations present (`AddTeamsChannel`, `AddSlackChannel`, `AddTelegramChannel`, `AddDiscordChannel`). App starts with all 4 channels when all tokens are configured.
  >
  > **Test — partial config:** Only configure Teams + Slack tokens → app starts with just those 2 channels, Telegram/Discord silently skipped.

- [ ] **3.7 Add Teams/Telegram/Discord config sections to `appsettings.json`**
  > **Test:** `appsettings.json` is valid JSON. Contains all 4 channel config blocks (`Teams`, `Slack`, `Telegram`, `Discord`).

- [ ] **3.8 Secrets documentation in CLAUDE.md**
  > **Test:** CLAUDE.md "Secrets" section includes all channel secrets:
  > - `dotnet user-secrets set "Teams:MicrosoftAppId" "xxx"`
  > - `dotnet user-secrets set "Teams:MicrosoftAppPassword" "xxx"`
  > - `dotnet user-secrets set "Teams:MicrosoftAppTenantId" "xxx"`
  > - `dotnet user-secrets set "Slack:BotToken" "xoxb-xxx"`
  > - `dotnet user-secrets set "Slack:AppToken" "xapp-xxx"`
  > - `dotnet user-secrets set "Slack:SigningSecret" "xxx"`
  > - `dotnet user-secrets set "Slack:BotUserId" "UXXXXX"`
  > - `dotnet user-secrets set "Telegram:BotToken" "xxx"`
  > - `dotnet user-secrets set "Discord:BotToken" "xxx"`

- [ ] **3.9 End-to-end: agent reachable on all 4 channels**
  > **Test scenario:**
  > 1. Send "Hello" via Teams → agent replies (session: `teams:{conversationId}`)
  > 2. Send "Hello" via Slack DM → agent replies (session: `slack:dm:{userId}`)
  > 3. @mention bot in Slack channel → agent replies in-thread (session: `slack:{ch}:{userId}`)
  > 4. Send "Hello" via Telegram DM → agent replies (session: `telegram:{chatId}`)
  > 5. Send "Hello" via Discord DM → agent replies (session: `discord:dm:{userId}`)
  > 6. `/status` on any channel → shows session ID with correct platform prefix
  > 7. `/health` → `ActiveSessions` count reflects sessions from multiple channels

---

### Improvement 4 — Make the Runtime Smart

- [ ] **4.1 Add `/reset` command to `ClawRuntime.cs`**
  > **Test:** Send `/reset` via Slack DM → response is "Session cleared. Starting fresh." → send a follow-up message → agent has no memory of previous conversation (new session).

- [ ] **4.2 Add `/status` command to `ClawRuntime.cs`**
  > **Test:** Send `/status` → response contains session ID, active status, creation time, and total request count.
  >
  > **Test — counter:** Send 3 messages → `/status` → total requests shows ≥ 3.

- [ ] **4.3 Add `/help` command to `ClawRuntime.cs`**
  > **Test:** Send `/help` → response lists all 3 commands (`/reset`, `/status`, `/help`) with descriptions.

- [ ] **4.4 Unknown slash commands pass through to agent**
  > **Test:** Send `/something_random hello` → agent receives the full message (not intercepted as a command) and responds normally.

- [ ] **4.5 Add Slack typing indicator (thinking_face reaction)**
  > **Test — visual:** Send a Slack message → observe 🤔 reaction appears within 1s → reaction disappears after agent replies.
  >
  > **Test — error resilience:** Delete the message while agent is thinking → no crash (reaction removal fails silently).

- [ ] **4.6 Add `/health` endpoint to `Program.cs`**
  > **Test:** `curl http://localhost:5000/health` → returns JSON with `Name`, `Status: "running"`, `Mind` path, `ActiveSessions` count, `TotalRequests`, and `Uptime`.
  >
  > **Test — after traffic:** Send a few messages via Slack → `curl /health` → `TotalRequests` > 0, `ActiveSessions` ≥ 1.

- [ ] **4.7 End-to-end: full conversation loop with commands + tools + memory**
  > **Test scenario:**
  > 1. `/help` → see commands
  > 2. "Remember I'm working on Project Alpha" → agent calls `SaveFactAsync`
  > 3. `/status` → see session active
  > 4. "What files are in the mind directory?" → agent calls `ListDirAsync`
  > 5. `/reset` → session cleared
  > 6. "What project am I working on?" → agent says "Project Alpha" (persisted in memory.md, survives reset)
  > 7. `/health` → API returns stats showing the requests above
