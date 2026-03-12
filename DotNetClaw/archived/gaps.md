# DotNetClaw Gap Analysis

> Comparing **DotNetClaw** (~400 LOC, C#/.NET 10) against **OpenClaw** (~430k LOC, TypeScript, 259k stars) and **nanobot** (~4k LOC core, Python, 28.6k stars).
>
> Each gap includes: what the reference projects do, what DotNetClaw is missing, a workflow diagram, C# pseudo code showing how to fill the gap, and which files to add or change.

---

## Feature Matrix

| Capability | OpenClaw | nanobot | DotNetClaw | Gap Severity |
|---|---|---|---|---|
| Channels | 22+ (Telegram, Discord, Slack, WhatsApp, Teams, Web…) | 10 (Telegram, Discord, WhatsApp, Slack, Feishu…) | 2 (WhatsApp, Slack) | **High** |
| LLM Providers | 10+ (OpenAI, Anthropic, Azure, Gemini, local…) | 15+ (OpenAI, Anthropic, Deepseek, Ollama…) | 1 (GitHub Copilot SDK only) | **High** |
| Built-in Tools | Plugin SDK, ClawHub skills, web search, exec, file | shell, filesystem, web, cron, spawn, MCP | None (tools: null) | **Critical** |
| Session Persistence | JSONL file store, session compaction, session:reset | SQLite / JSON file, /reset, /save, /load | In-memory only (ConcurrentDictionary) | **Critical** |
| Security | DM-pairing codes, API key auth, Docker sandbox | Workspace restriction, dangerous-cmd filter, role checks | Twilio HMAC + Slack allowlist only | **High** |
| Memory | Persistent agent memory, session compaction | memory.py — cross-session recall | Planned (.working-memory/) but not wired | **Medium** |
| Automation | Cron, webhooks, Gmail pubsub | cron.py, heartbeat, spawn subagent | None | **Medium** |
| Observability | Gateway dashboard, structured logging, usage tracking | Structured logs, heartbeat health | Basic ILogger, no metrics | **Medium** |
| Deployment | Docker Compose, companion apps, managed cloud | Docker Compose, pip install | Aspire AppHost (local only) | **Low** |
| Extensibility | Plugin SDK + npm registry, Canvas/A2UI | MCP (stdio + HTTP), skills YAML | No plugin/MCP/skill system | **High** |

---

## Gap 1 — Channel Coverage

### What the references do

- **OpenClaw**: 22+ channels. Each adapter converts platform events → `{ sessionId, text }` → Gateway. Adapters are in `src/<platform>/` directories with a common interface.
- **nanobot**: 10 channels. Each under `channels/`, inherits `BaseChannel`, overrides `send()`, `receive()`, and `format()`.

### What DotNetClaw has

Two channels: `WhatsAppChannel.cs` (Twilio webhook) and `SlackChannel.cs` (SlackNet socket mode). Each is an extension method that registers endpoints/handlers and calls `runtime.HandleAsync(sessionId, text, ct)`.

### Workflow to fill

```
                     IChannel interface
                           │
            ┌──────────────┼──────────────┐
            │              │              │
   TelegramChannel  DiscordChannel  TeamsChannel
            │              │              │
   Telegram.Bot     Discord.Net     MS Bot Framework
   (polling)        (gateway WS)   (Activity → text)
            │              │              │
            └──────────────┼──────────────┘
                           │
                   runtime.HandleAsync(sessionId, text, ct)
```

### Pseudo code

```csharp
// ── IChannel.cs (new file) ──────────────────────────────────────────────
public interface IChannel
{
    /// <summary>Platform name used in session IDs, e.g. "telegram", "discord".</summary>
    string PlatformName { get; }

    /// <summary>Register DI services for this channel.</summary>
    static abstract IServiceCollection AddChannel(IServiceCollection services, IConfiguration config);

    /// <summary>Map HTTP endpoints or start background listeners.</summary>
    static abstract WebApplication MapChannel(WebApplication app);
}

// ── TelegramChannel.cs (new file) ──────────────────────────────────────
// NuGet: Telegram.Bot
public sealed class TelegramChannel : BackgroundService, IChannel
{
    public string PlatformName => "telegram";

    private readonly ClawRuntime _runtime;
    private readonly TelegramBotClient _bot;

    public TelegramChannel(ClawRuntime runtime, IConfiguration config)
    {
        _runtime = runtime;
        _bot = new TelegramBotClient(config["Telegram:BotToken"]!);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var offset = 0;
        while (!ct.IsCancellationRequested)
        {
            var updates = await _bot.GetUpdatesAsync(offset, cancellationToken: ct);
            foreach (var u in updates.Where(u => u.Message?.Text != null))
            {
                var sessionId = $"telegram:{u.Message!.Chat.Id}";
                var reply = await _runtime.HandleAsync(sessionId, u.Message.Text!, ct);
                if (!string.IsNullOrWhiteSpace(reply))
                    await _bot.SendTextMessageAsync(u.Message.Chat.Id, reply, cancellationToken: ct);
                offset = u.Id + 1;
            }
        }
    }
}

// ── DiscordChannel.cs (new file) ───────────────────────────────────────
// NuGet: Discord.Net
public sealed class DiscordChannel : BackgroundService
{
    private readonly ClawRuntime _runtime;
    private readonly DiscordSocketClient _client;

    public DiscordChannel(ClawRuntime runtime, IConfiguration config)
    {
        _runtime = runtime;
        _client = new DiscordSocketClient();
        _client.MessageReceived += async msg =>
        {
            if (msg.Author.IsBot) return;
            var sessionId = $"discord:{msg.Channel.Id}:{msg.Author.Id}";
            var reply = await _runtime.HandleAsync(sessionId, msg.Content, default);
            if (!string.IsNullOrWhiteSpace(reply))
                await msg.Channel.SendMessageAsync(reply);
        };
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _client.LoginAsync(TokenType.Bot, /* config["Discord:BotToken"] */);
        await _client.StartAsync();
        await Task.Delay(Timeout.Infinite, ct);
    }
}

// ── Program.cs registration ────────────────────────────────────────────
// builder.Services.AddHostedService<TelegramChannel>();
// builder.Services.AddHostedService<DiscordChannel>();
```

**Files to add**: `IChannel.cs`, `TelegramChannel.cs`, `DiscordChannel.cs`, `TeamsChannel.cs`
**Files to change**: `Program.cs` (register new channels), `appsettings.json` (add config sections)
**NuGet packages**: `Telegram.Bot`, `Discord.Net`, `Microsoft.Bot.Builder`

---

## Gap 2 — Multi-Provider LLM Support

### What the references do

- **OpenClaw**: Provider registry with 10+ backends (OpenAI, Anthropic, Azure OpenAI, Gemini, local). Each provider implements a common chat-completion interface. Failover, model routing, OAuth token refresh.
- **nanobot**: `providers/` directory with 15+ adapters. `providers/__init__.py` maps provider names to classes. Config picks provider per agent via `provider:` key.

### What DotNetClaw has

Single provider: GitHub Copilot SDK (`CopilotClient.AsAIAgent()`). Model is set in config but the actual overload used doesn't pass `SessionConfig`, so it's locked to the Copilot backend.

### Workflow to fill

```
appsettings.json          IAgentProvider interface
  "Provider": "copilot"        │
       │               ┌───────┼───────┐
       │               │       │       │
       ▼         CopilotProv  OpenAI  Anthropic
  ProviderFactory              │       │
       │            uses MAF  uses    uses
       ▼            bridge    MEAI    MEAI
  returns AIAgent              │       │
                    ┌──────────┘       │
                    ▼                  ▼
              Microsoft.Extensions.AI.IChatClient
                    │
              MAF ChatClientAIAgent
```

### Pseudo code

```csharp
// ── IAgentProvider.cs (new file) ───────────────────────────────────────
public interface IAgentProvider
{
    string Name { get; }
    Task<AIAgent> CreateAgentAsync(
        string systemMessage, IEnumerable<AITool>? tools, CancellationToken ct = default);
}

// ── CopilotProvider.cs (new file) ──────────────────────────────────────
// Wraps existing logic from Program.cs
public sealed class CopilotProvider(MindLoader mind) : IAgentProvider
{
    public string Name => "copilot";

    public Task<AIAgent> CreateAgentAsync(
        string systemMessage, IEnumerable<AITool>? tools, CancellationToken ct)
    {
        var client = new CopilotClient(new CopilotClientOptions
        {
            Cwd = mind.MindRoot,
            AutoStart = true,
            UseStdio = true,
        });

        return Task.FromResult(client.AsAIAgent(
            ownsClient: true,
            id: "dotnetclaw",
            name: "DotNetClaw",
            description: "Personal AI assistant",
            tools: tools?.ToList(),
            instructions: systemMessage));
    }
}

// ── OpenAIProvider.cs (new file) ───────────────────────────────────────
// NuGet: Microsoft.Extensions.AI.OpenAI
public sealed class OpenAIProvider(IConfiguration config) : IAgentProvider
{
    public string Name => "openai";

    public Task<AIAgent> CreateAgentAsync(
        string systemMessage, IEnumerable<AITool>? tools, CancellationToken ct)
    {
        IChatClient chatClient = new OpenAI.Chat.ChatClient(
            config["OpenAI:Model"] ?? "gpt-4o",
            config["OpenAI:ApiKey"])
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        // MAF provides ChatClientAIAgent to wrap any IChatClient → AIAgent
        var agent = new ChatClientAIAgent(chatClient, systemMessage, tools);
        return Task.FromResult<AIAgent>(agent);
    }
}

// ── ProviderFactory.cs (new file) ──────────────────────────────────────
public static class ProviderFactory
{
    public static IAgentProvider Resolve(
        string providerName, IServiceProvider sp) => providerName switch
    {
        "copilot"   => sp.GetRequiredService<CopilotProvider>(),
        "openai"    => sp.GetRequiredService<OpenAIProvider>(),
        "anthropic" => sp.GetRequiredService<AnthropicProvider>(),
        _           => throw new ArgumentException($"Unknown provider: {providerName}")
    };
}

// ── Program.cs change ──────────────────────────────────────────────────
// Replace the direct CopilotClient setup with:
builder.Services.AddSingleton<CopilotProvider>();
builder.Services.AddSingleton<OpenAIProvider>();
builder.Services.AddSingleton<AIAgent>(sp =>
{
    var mind     = sp.GetRequiredService<MindLoader>();
    var sysMsg   = mind.LoadSystemMessageAsync().GetAwaiter().GetResult();
    var provider = sp.GetRequiredService<IConfiguration>()["Provider"] ?? "copilot";
    return ProviderFactory.Resolve(provider, sp)
        .CreateAgentAsync(sysMsg, tools: null, default).GetAwaiter().GetResult();
});
```

**Files to add**: `IAgentProvider.cs`, `CopilotProvider.cs`, `OpenAIProvider.cs`, `AnthropicProvider.cs`, `ProviderFactory.cs`
**Files to change**: `Program.cs` (DI refactor), `appsettings.json` (add `Provider` + API keys)
**NuGet packages**: `Microsoft.Extensions.AI.OpenAI`, `Microsoft.Extensions.AI.Abstractions`

---

## Gap 3 — Built-in Tools (Agent Can't Act)

### What the references do

- **OpenClaw**: Plugin SDK, ClawHub skill store, web search, shell exec, file I/O. Tools are registered in the agent config and discovered at startup.
- **nanobot**: `agent/tools/` — `shell.py` (command exec), `filesystem.py` (read/write/list), `web.py` (HTTP fetch), `mcp.py` (MCP bridge), `cron.py`, `message.py`, `spawn.py`. Each tool is a Python function decorated with metadata.

### What DotNetClaw has

`tools: null` in `Program.cs`. The agent can only converse — it cannot execute commands, read files, search the web, or interact with external systems.

### Workflow to fill

```
Program.cs
    │
    ├── AIFunctionFactory.Create(execTool.RunAsync)     ← shell exec
    ├── AIFunctionFactory.Create(fsTool.ReadFileAsync)   ← file read
    ├── AIFunctionFactory.Create(fsTool.WriteFileAsync)  ← file write
    ├── AIFunctionFactory.Create(fsTool.ListDirAsync)    ← directory listing
    ├── AIFunctionFactory.Create(webTool.FetchAsync)     ← HTTP fetch
    └── AIFunctionFactory.Create(memTool.SaveFactAsync)  ← memory (see Gap 6)
    │
    ▼
tools list passed to copilotClient.AsAIAgent(..., tools: tools, ...)
    │
    ▼
MAF ReAct loop auto-calls tools when agent decides to
```

### Pseudo code

```csharp
// ── ExecTool.cs (new file) ─────────────────────────────────────────────
using System.ComponentModel;
using System.Diagnostics;

public sealed class ExecTool
{
    // Dangerous-command filter (nanobot pattern: SECURITY.md)
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm -rf /", "mkfs", "dd if=", ":(){ :|:& };:", "shutdown", "reboot"
    };

    [Description("Execute a shell command and return stdout + stderr. " +
                 "Use for build, test, git, and system tasks.")]
    public async Task<string> RunAsync(
        [Description("The shell command to execute")] string command,
        CancellationToken ct = default)
    {
        if (Blocked.Any(b => command.Contains(b, StringComparison.OrdinalIgnoreCase)))
            return "⚠ Command blocked by safety filter.";

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var result = $"exit={proc.ExitCode}\n{stdout}";
        if (!string.IsNullOrWhiteSpace(stderr))
            result += $"\nSTDERR:\n{stderr}";

        return result.Length > 8000 ? result[..8000] + "\n[truncated]" : result;
    }
}

// ── FileSystemTool.cs (new file) ───────────────────────────────────────
using System.ComponentModel;

public sealed class FileSystemTool
{
    private readonly string _workspaceRoot;

    public FileSystemTool(string workspaceRoot)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    [Description("Read the contents of a file.")]
    public async Task<string> ReadFileAsync(
        [Description("Relative path to the file")] string path,
        CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(path);
        if (!File.Exists(fullPath)) return $"File not found: {path}";
        var content = await File.ReadAllTextAsync(fullPath, ct);
        return content.Length > 10000 ? content[..10000] + "\n[truncated]" : content;
    }

    [Description("Write content to a file. Creates parent directories if needed.")]
    public async Task<string> WriteFileAsync(
        [Description("Relative path to the file")] string path,
        [Description("Content to write")] string content,
        CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content, ct);
        return $"Written {content.Length} chars to {path}";
    }

    [Description("List files and directories at the given path.")]
    public Task<string> ListDirAsync(
        [Description("Relative directory path (default: root)")] string path = ".",
        CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(path);
        if (!Directory.Exists(fullPath)) return Task.FromResult($"Directory not found: {path}");

        var entries = Directory.GetFileSystemEntries(fullPath)
            .Select(e => Path.GetRelativePath(_workspaceRoot, e))
            .OrderBy(e => e);
        return Task.FromResult(string.Join('\n', entries));
    }

    // Prevent path traversal outside workspace
    private string ResolveSafe(string path)
    {
        var full = Path.GetFullPath(Path.Combine(_workspaceRoot, path));
        if (!full.StartsWith(_workspaceRoot))
            throw new UnauthorizedAccessException("Path escapes workspace boundary.");
        return full;
    }
}

// ── WebSearchTool.cs (new file) ────────────────────────────────────────
using System.ComponentModel;

public sealed class WebSearchTool(IHttpClientFactory httpFactory)
{
    [Description("Fetch the text content of a URL.")]
    public async Task<string> FetchAsync(
        [Description("The URL to fetch")] string url,
        CancellationToken ct = default)
    {
        using var client = httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        var response = await client.GetStringAsync(url, ct);
        return response.Length > 10000 ? response[..10000] + "\n[truncated]" : response;
    }
}

// ── Program.cs change — register tools ─────────────────────────────────
builder.Services.AddHttpClient();

// In the AIAgent factory:
var execTool = new ExecTool();
var fsTool   = new FileSystemTool(mind.MindRoot);
var webTool  = new WebSearchTool(sp.GetRequiredService<IHttpClientFactory>());

var tools = new List<AITool>
{
    AIFunctionFactory.Create(execTool.RunAsync),
    AIFunctionFactory.Create(fsTool.ReadFileAsync),
    AIFunctionFactory.Create(fsTool.WriteFileAsync),
    AIFunctionFactory.Create(fsTool.ListDirAsync),
    AIFunctionFactory.Create(webTool.FetchAsync),
};

return copilotClient.AsAIAgent(
    ownsClient: true,
    id: "dotnetclaw",
    name: "DotNetClaw",
    description: "Personal AI assistant on WhatsApp and Slack",
    tools: tools,          // ← was null
    instructions: systemMessage);
```

**Files to add**: `ExecTool.cs`, `FileSystemTool.cs`, `WebSearchTool.cs`
**Files to change**: `Program.cs` (register tools in AIAgent factory)
**NuGet packages**: None (uses `System.Diagnostics.Process`, `System.IO`, `IHttpClientFactory`)

---

## Gap 4 — Session Persistence & Chat Commands

### What the references do

- **OpenClaw**: JSONL session store on disk. Sessions survive restarts. Built-in commands: `session:reset` clears context, `session:list` shows active sessions. Session compaction trims old turns when context grows too large.
- **nanobot**: SQLite / JSON file persistence. `/reset` clears, `/save` exports, `/load` imports, `/status` shows session info. Automatic session compaction after N turns.

### What DotNetClaw has

`ConcurrentDictionary<string, AgentSession>` in `ClawRuntime.cs` — all sessions are lost on restart. No chat commands. No compaction. No `/reset`.

### Workflow to fill

```
User sends "/reset"
      │
      ▼
ClawRuntime.HandleAsync()
      │
      ├── if text starts with "/"
      │     ├── "/reset"  → _sessions.Remove(sessionId) + reply "Session cleared"
      │     ├── "/status" → reply session info (turns, created, provider)
      │     └── "/help"   → reply command list
      │
      └── else → normal agent flow
              │
              ▼
        SessionStore.SaveAsync(sessionId, turns)  ← persist after each exchange
              │
              ▼
        On startup: SessionStore.LoadAllAsync() → restore _sessions
```

### Pseudo code

```csharp
// ── SessionStore.cs (new file) ─────────────────────────────────────────
using System.Text.Json;

public sealed class SessionStore
{
    private readonly string _dir;

    public SessionStore(string baseDir = ".sessions")
    {
        _dir = Path.GetFullPath(baseDir);
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Persist the session's conversation history as a JSONL file.</summary>
    public async Task SaveAsync(string sessionId, IReadOnlyList<ChatMessage> turns, CancellationToken ct = default)
    {
        var safeName = SanitizeFileName(sessionId);
        var path = Path.Combine(_dir, $"{safeName}.jsonl");

        await using var writer = new StreamWriter(path, append: false);
        foreach (var turn in turns)
        {
            var line = JsonSerializer.Serialize(new { turn.Role, turn.Content, turn.Timestamp });
            await writer.WriteLineAsync(line.AsMemory(), ct);
        }
    }

    /// <summary>Load a persisted session. Returns empty list if not found.</summary>
    public async Task<List<ChatMessage>> LoadAsync(string sessionId, CancellationToken ct = default)
    {
        var safeName = SanitizeFileName(sessionId);
        var path = Path.Combine(_dir, $"{safeName}.jsonl");
        if (!File.Exists(path)) return [];

        var turns = new List<ChatMessage>();
        await foreach (var line in File.ReadLinesAsync(path, ct))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var msg = JsonSerializer.Deserialize<ChatMessage>(line);
            if (msg != null) turns.Add(msg);
        }
        return turns;
    }

    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, $"{SanitizeFileName(sessionId)}.jsonl");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private static string SanitizeFileName(string sessionId) =>
        string.Join("_", sessionId.Split(Path.GetInvalidFileNameChars()));
}

// ── ClawRuntime.cs changes — add command interception ──────────────────
public async Task<string> HandleAsync(string sessionId, string message, CancellationToken ct = default)
{
    // ── Chat command interception (nanobot pattern) ──
    if (message.StartsWith('/'))
    {
        var cmd = message.Split(' ', 2)[0].ToLowerInvariant();
        return cmd switch
        {
            "/reset"  => await ResetSessionAsync(sessionId, ct),
            "/status" => GetSessionStatus(sessionId),
            "/help"   => "Commands: /reset — clear session, /status — session info, /help — this message",
            _         => await RunAgentAsync(sessionId, message, ct)  // not a known command, pass through
        };
    }

    return await RunAgentAsync(sessionId, message, ct);
}

private async Task<string> ResetSessionAsync(string sessionId, CancellationToken ct)
{
    _sessions.TryRemove(sessionId, out _);
    _locks.TryRemove(sessionId, out _);
    await _store.DeleteAsync(sessionId, ct);
    return "Session cleared. Starting fresh.";
}

private string GetSessionStatus(string sessionId)
{
    if (!_sessions.TryGetValue(sessionId, out var session))
        return "No active session.";
    return $"Session: {sessionId}\nActive: yes\nProvider: Copilot SDK";
}
```

**Files to add**: `SessionStore.cs`
**Files to change**: `ClawRuntime.cs` (add command interception, inject SessionStore), `Program.cs` (register SessionStore)

---

## Gap 5 — Security & Access Control

### What the references do

- **OpenClaw**: DM-pairing system (user must type a secret code in DM before the bot responds). API key authentication for API channels. Docker sandbox for tool execution (code runs in isolated containers). Rate limiting per session.
- **nanobot**: Workspace restriction (tools can't escape root dir). Dangerous command filtering (blocklist of destructive patterns). Role-based channel access. Per-user rate limiting.

### What DotNetClaw has

- WhatsApp: Twilio `ValidateTwilioRequestFilter` (HMAC check on webhook) — verifies the request came from Twilio, but **any WhatsApp user can talk to the bot**.
- Slack: Configurable policy (`"open"` or `"allowlist"`) with allowed user IDs — good but not replicated on WhatsApp.

### Workflow to fill

```
Inbound message
      │
      ▼
  RateLimiter.IsAllowed(sessionId)?
      │ no → "Rate limit exceeded. Try again in X seconds."
      │ yes
      ▼
  AccessPolicy.IsAllowed(platform, userId)?
      │ no → "Not authorized. Use /pair <code> to start."
      │ yes
      ▼
  PairingService.IsPaired(sessionId)?
      │ no → "Send the pairing code from your admin."
      │ yes
      ▼                                 Tool execution
  ClawRuntime.HandleAsync()             ──────────────
      │                                       │
      ▼                                       ▼
  Agent response                    SandboxedExec (Docker/nsjail)
                                    or WorkspaceRestriction (path check)
```

### Pseudo code

```csharp
// ── SessionRateLimiter.cs (new file) ───────────────────────────────────
using System.Collections.Concurrent;

public sealed class SessionRateLimiter
{
    private readonly ConcurrentDictionary<string, (int Count, DateTime Window)> _buckets = new();
    private readonly int _maxPerWindow;
    private readonly TimeSpan _windowSize;

    public SessionRateLimiter(int maxPerWindow = 30, TimeSpan? windowSize = null)
    {
        _maxPerWindow = maxPerWindow;
        _windowSize = windowSize ?? TimeSpan.FromMinutes(1);
    }

    public bool IsAllowed(string sessionId)
    {
        var now = DateTime.UtcNow;
        var bucket = _buckets.AddOrUpdate(sessionId,
            _ => (1, now),
            (_, old) => now - old.Window > _windowSize
                ? (1, now)                         // window expired, reset
                : (old.Count + 1, old.Window));    // same window, increment

        return bucket.Count <= _maxPerWindow;
    }
}

// ── PairingService.cs (new file) ───────────────────────────────────────
// OpenClaw's DM-pairing pattern: admin generates a code, user sends it to bot in DM
using System.Collections.Concurrent;

public sealed class PairingService
{
    private readonly ConcurrentDictionary<string, string> _pendingCodes = new(); // code → targetSessionId
    private readonly ConcurrentDictionary<string, bool> _paired = new();

    public bool IsPaired(string sessionId) => _paired.ContainsKey(sessionId);

    public string GenerateCode(string sessionId)
    {
        var code = Guid.NewGuid().ToString("N")[..8].ToUpper();
        _pendingCodes[code] = sessionId;
        return code;
    }

    public bool TryPair(string sessionId, string code)
    {
        if (_pendingCodes.TryRemove(code, out var target) && target == sessionId)
        {
            _paired[sessionId] = true;
            return true;
        }
        return false;
    }
}

// ── WhatsAppChannel.cs change — add allowlist + rate limit ─────────────
var from = form["From"].ToString();
var body = form["Body"].ToString();

// Rate limit check
if (!rateLimiter.IsAllowed(from))
    return Results.Ok(); // silently drop (or reply "rate limited")

// WhatsApp allowlist (same pattern as SlackChannel)
var whatsappPolicy = config["WhatsApp:Policy"] ?? "open";
if (whatsappPolicy == "allowlist")
{
    var allowed = new HashSet<string>(
        (config["WhatsApp:AllowedNumbers"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries));
    if (!allowed.Contains(from))
        return Results.Ok(); // not authorized
}

// Pairing check (OpenClaw pattern)
if (config.GetValue<bool>("Security:RequirePairing") && !pairingService.IsPaired(from))
{
    if (body.StartsWith("/pair "))
    {
        var code = body[6..].Trim();
        var paired = pairingService.TryPair(from, code);
        // Send response via Twilio...
        return Results.Ok();
    }
    // Send "pair first" message...
    return Results.Ok();
}
```

**Files to add**: `SessionRateLimiter.cs`, `PairingService.cs`
**Files to change**: `WhatsAppChannel.cs` (add allowlist + rate limit + pairing), `ClawRuntime.cs` (inject rate limiter), `appsettings.json` (add `WhatsApp:Policy`, `Security:RequirePairing`)

---

## Gap 6 — Persistent Memory

### What the references do

- **OpenClaw**: Agent memory persists across sessions. The agent can save/recall facts. Session compaction summarizes old turns to stay within context limits.
- **nanobot**: `memory.py` — agent can explicitly save facts (`save_memory`) and recall them (`recall_memory`). Facts are stored in a JSON file and injected into context on each turn.

### What DotNetClaw has

The `mind/.working-memory/memory.md` path is documented in `MindLoader.cs` comments but **not implemented**. The agent has no way to persist or recall facts across sessions.

### Workflow to fill

```
Agent decides to remember something
      │
      ▼
MemoryTool.SaveFactAsync("User prefers dark mode")
      │
      ▼
Appends to mind/.working-memory/memory.md
      ╷
      ╵
Next conversation turn
      │
      ▼
MindLoader.LoadSystemMessageAsync()
      │
      ├── SOUL.md
      ├── *.agent.md
      └── .working-memory/memory.md  ← NEW: appended to system message
      │
      ▼
Agent context now includes remembered facts
```

### Pseudo code

```csharp
// ── MemoryTool.cs (new file) ───────────────────────────────────────────
using System.ComponentModel;

public sealed class MemoryTool
{
    private readonly string _memoryPath;

    public MemoryTool(MindLoader mind)
    {
        _memoryPath = Path.Combine(mind.MindRoot, ".working-memory", "memory.md");
        Directory.CreateDirectory(Path.GetDirectoryName(_memoryPath)!);
    }

    [Description("Save a fact to long-term memory. The agent will remember this across sessions.")]
    public async Task<string> SaveFactAsync(
        [Description("The fact to remember, e.g. 'User prefers dark mode'")] string fact,
        CancellationToken ct = default)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");
        var entry = $"- [{timestamp}] {fact}\n";
        await File.AppendAllTextAsync(_memoryPath, entry, ct);
        return $"Remembered: {fact}";
    }

    [Description("Recall all saved facts from long-term memory.")]
    public async Task<string> RecallFactsAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_memoryPath))
            return "No memories saved yet.";
        var content = await File.ReadAllTextAsync(_memoryPath, ct);
        return string.IsNullOrWhiteSpace(content) ? "No memories saved yet." : content;
    }
}

// ── MindLoader.cs change — inject memory into system message ───────────
public async Task<string> LoadSystemMessageAsync(CancellationToken ct = default)
{
    // ... existing SOUL.md + agent file loading ...

    // NEW: Append working memory if it exists
    var memoryPath = Path.Combine(_mindRoot, ".working-memory", "memory.md");
    if (File.Exists(memoryPath))
    {
        var memory = await File.ReadAllTextAsync(memoryPath, ct);
        if (!string.IsNullOrWhiteSpace(memory))
        {
            parts.Add("## Working Memory\n\nFacts you have previously saved:\n\n" + memory);
        }
    }

    return string.Join("\n\n---\n\n", parts);
}

// ── Program.cs — register memory tool ──────────────────────────────────
var memTool = new MemoryTool(mind);
tools.Add(AIFunctionFactory.Create(memTool.SaveFactAsync));
tools.Add(AIFunctionFactory.Create(memTool.RecallFactsAsync));
```

**Files to add**: `MemoryTool.cs`
**Files to change**: `MindLoader.cs` (load .working-memory/memory.md into system message), `Program.cs` (register memory tools)

---

## Gap 7 — Automation (Cron & Webhooks)

### What the references do

- **OpenClaw**: Cron schedules (send messages on schedule), webhook endpoints (external triggers), Gmail pubsub (email → agent).
- **nanobot**: `agent/tools/cron.py` (schedule recurring tasks), `heartbeat/` (periodic health pings), `agent/tools/spawn.py` (subagent creation).

### What DotNetClaw has

Nothing. No scheduled tasks, no external triggers, no webhook-to-agent pipeline.

### Workflow to fill

```
appsettings.json                         External system
  "Cron": [                                   │
    { "Schedule": "0 9 * * *",                │ POST /webhooks/{name}
      "SessionId": "cron:daily",              │
      "Message": "Morning summary" }          ▼
  ]                                    WebhookEndpoints.cs
      │                                       │
      ▼                                       ▼
CronService (BackgroundService)     runtime.HandleAsync(webhookSessionId, payload, ct)
      │
  NCrontab.Parse(schedule)
  Timer fires at next occurrence
      │
      ▼
runtime.HandleAsync(sessionId, message, ct)
      │
      ▼
reply → (optional: send to a channel or store result)
```

### Pseudo code

```csharp
// ── CronService.cs (new file) ──────────────────────────────────────────
// NuGet: NCrontab
using NCrontab;

public sealed class CronService(
    ClawRuntime runtime,
    IConfiguration config,
    ILogger<CronService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var jobs = config.GetSection("Cron").Get<List<CronJobConfig>>() ?? [];
        if (jobs.Count == 0)
        {
            logger.LogInformation("No cron jobs configured.");
            return;
        }

        logger.LogInformation("Starting {Count} cron jobs", jobs.Count);

        while (!ct.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            foreach (var job in jobs)
            {
                var schedule = CrontabSchedule.Parse(job.Schedule);
                var next = schedule.GetNextOccurrence(now);
                if (next - now < TimeSpan.FromSeconds(30))
                {
                    logger.LogInformation("[Cron] Firing: {Session} → {Message}", job.SessionId, job.Message);
                    try
                    {
                        await runtime.HandleAsync(job.SessionId, job.Message, ct);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[Cron] Job {Session} failed", job.SessionId);
                    }
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }
}

public record CronJobConfig
{
    public string Schedule  { get; init; } = "";   // cron expression
    public string SessionId { get; init; } = "";   // e.g. "cron:daily-summary"
    public string Message   { get; init; } = "";   // text sent to agent
}

// ── WebhookEndpoints.cs (new file) ─────────────────────────────────────
public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/{name}", async (
            string name,
            HttpContext ctx,
            ClawRuntime runtime,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var payload = await reader.ReadToEndAsync(ct);

            var sessionId = $"webhook:{name}";
            var message = $"Webhook '{name}' triggered with payload:\n{payload}";

            var reply = await runtime.HandleAsync(sessionId, message, ct);
            return Results.Ok(new { reply });
        });

        return app;
    }
}

// ── Program.cs ─────────────────────────────────────────────────────────
builder.Services.AddHostedService<CronService>();
// ...
app.MapWebhooks();
```

**Files to add**: `CronService.cs`, `WebhookEndpoints.cs`
**Files to change**: `Program.cs` (register CronService, map webhook endpoints), `appsettings.json` (add Cron section)
**NuGet packages**: `NCrontab`

---

## Gap 8 — Observability & UX Polish

### What the references do

- **OpenClaw**: Gateway dashboard, structured logging with correlation IDs, usage tracking (tokens, cost), typing indicators on all channels, rich error messages.
- **nanobot**: Structured logs, heartbeat health check, per-agent metrics, typing indicators (Telegram `sendChatAction`, Discord `typing()`).

### What DotNetClaw has

Basic `ILogger` with `LogDebug`/`LogInformation`. No metrics, no typing indicators, no usage tracking. The `/` health endpoint returns only `{ name, status }`.

### Workflow to fill

```
Agent processing starts
      │
      ├── Slack: slack.Reactions.AddAsync("thinking_face")
      │   or: slack.Conversations.SetTypingAsync(channel)
      │
      ├── WhatsApp: (Twilio doesn't support typing natively)
      │
      ├── UsageTracker.RecordRequest(sessionId, inputTokens, outputTokens)
      │
      ▼
Agent processing completes
      │
      ├── Slack: slack.Reactions.RemoveAsync("thinking_face")
      │
      ├── logger.LogInformation with structured properties
      │   { SessionId, Duration, InputLen, OutputLen, Provider }
      │
      └── /health endpoint: { uptime, activeSessions, totalRequests }
```

### Pseudo code

```csharp
// ── UsageTracker.cs (new file) ─────────────────────────────────────────
using System.Collections.Concurrent;

public sealed class UsageTracker
{
    private long _totalRequests;
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, SessionStats> _stats = new();

    public void RecordRequest(string sessionId, int inputLen, int outputLen, TimeSpan duration)
    {
        Interlocked.Increment(ref _totalRequests);
        _stats.AddOrUpdate(sessionId,
            _ => new SessionStats { Requests = 1, TotalInputChars = inputLen, TotalOutputChars = outputLen },
            (_, old) => old with
            {
                Requests = old.Requests + 1,
                TotalInputChars = old.TotalInputChars + inputLen,
                TotalOutputChars = old.TotalOutputChars + outputLen,
            });
    }

    public object GetHealthReport() => new
    {
        Uptime = DateTime.UtcNow - _startedAt,
        TotalRequests = _totalRequests,
        ActiveSessions = _stats.Count,
    };

    public record SessionStats
    {
        public int Requests { get; init; }
        public int TotalInputChars { get; init; }
        public int TotalOutputChars { get; init; }
    }
}

// ── SlackChannel.cs change — add typing indicator ──────────────────────
public async Task Handle(MessageEvent e)
{
    // ... existing validation ...

    // Add thinking indicator (nanobot/OpenClaw pattern)
    await slack.Reactions.AddReaction(e.Channel, e.Ts, "thinking_face");
    try
    {
        var reply = await runtime.HandleAsync(sessionId, e.Text, default);
        // ... send reply ...
    }
    finally
    {
        // Remove thinking indicator regardless of success/failure
        try { await slack.Reactions.RemoveReaction(e.Channel, e.Ts, "thinking_face"); }
        catch { /* ignore — message may have been deleted */ }
    }
}

// ── Program.cs — enhanced health endpoint ──────────────────────────────
builder.Services.AddSingleton<UsageTracker>();
// ...
app.MapGet("/health", (UsageTracker tracker) => Results.Ok(tracker.GetHealthReport()));
```

**Files to add**: `UsageTracker.cs`
**Files to change**: `SlackChannel.cs` (typing indicators), `ClawRuntime.cs` (record usage), `Program.cs` (register UsageTracker, add /health)

---

## Gap 9 — Production Deployment

### What the references do

- **OpenClaw**: Docker Compose with Gateway + Redis + agents. Companion apps for macOS/iOS/Android. Managed cloud offering. CI/CD pipelines.
- **nanobot**: `Dockerfile` + `docker-compose.yml`. `pip install nanobot` for local. Single-binary distribution.

### What DotNetClaw has

Aspire AppHost for local dev (`DotNetClaw.AppHost/Program.cs`). No Dockerfile, no docker-compose, no production deployment story.

### Workflow to fill

```
Developer runs:
  docker compose up
      │
      ├── dotnetclaw service
      │     ├── multi-stage Dockerfile (build → runtime)
      │     ├── reads config from environment variables
      │     └── exposes port 5000
      │
      ├── tunnel service (optional)
      │     └── ngrok/cloudflared sidecar for WhatsApp webhook
      │
      └── (future: redis for session store, postgres for long-term memory)
```

### Pseudo code

```dockerfile
# ── Dockerfile (new file) ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY DotNetClaw/DotNetClaw.csproj DotNetClaw/
RUN dotnet restore DotNetClaw/DotNetClaw.csproj
COPY DotNetClaw/ DotNetClaw/
COPY mind/ mind/
RUN dotnet publish DotNetClaw/DotNetClaw.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
COPY mind/ ./mind/

# gh CLI for Copilot backend
RUN apt-get update && apt-get install -y gh && rm -rf /var/lib/apt/lists/*

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "DotNetClaw.dll"]
```

```yaml
# ── docker-compose.yml (new file) ──────────────────────────────────────
services:
  dotnetclaw:
    build:
      context: .
      dockerfile: Dockerfile
    ports:
      - "5000:5000"
    environment:
      - Twilio__AccountSid=${TWILIO_ACCOUNT_SID}
      - Twilio__AuthToken=${TWILIO_AUTH_TOKEN}
      - Twilio__From=${TWILIO_FROM}
      - Slack__BotToken=${SLACK_BOT_TOKEN}
      - Slack__AppToken=${SLACK_APP_TOKEN}
      - Slack__BotUserId=${SLACK_BOT_USER_ID}
      - Mind__Path=./mind
    volumes:
      - sessions:/app/.sessions
      - mind-memory:/app/mind/.working-memory

  tunnel:
    image: ngrok/ngrok:latest
    command: http dotnetclaw:5000
    environment:
      - NGROK_AUTHTOKEN=${NGROK_AUTHTOKEN}
    ports:
      - "4040:4040"

volumes:
  sessions:
  mind-memory:
```

```csharp
// ── DoctorEndpoints.cs (new file) — deployment health checks ───────────
public static class DoctorEndpoints
{
    public static IEndpointRouteBuilder MapDoctor(this IEndpointRouteBuilder app)
    {
        app.MapGet("/doctor", async (IConfiguration config) =>
        {
            var checks = new Dictionary<string, string>();

            // Check Copilot CLI binary
            var copilotPath = Path.Combine(AppContext.BaseDirectory, "copilot");
            checks["copilot-cli"] = File.Exists(copilotPath) ? "ok" : "missing";

            // Check mind directory
            var mindPath = config["Mind:Path"] ?? "./mind";
            checks["mind-soul"] = File.Exists(Path.Combine(mindPath, "SOUL.md")) ? "ok" : "missing";

            // Check secrets
            checks["twilio"] = !string.IsNullOrEmpty(config["Twilio:AccountSid"]) ? "ok" : "missing";
            checks["slack"]  = !string.IsNullOrEmpty(config["Slack:BotToken"]) ? "ok" : "missing";

            var allOk = checks.Values.All(v => v == "ok");
            return Results.Json(new { healthy = allOk, checks }, statusCode: allOk ? 200 : 503);
        });

        return app;
    }
}

// ── Program.cs ─────────────────────────────────────────────────────────
app.MapDoctor();
```

**Files to add**: `Dockerfile`, `docker-compose.yml`, `DoctorEndpoints.cs`, `.env.example`
**Files to change**: `Program.cs` (map doctor endpoint)

---

## Gap 10 — Extensibility (MCP & Skills)

### What the references do

- **OpenClaw**: Plugin SDK (npm packages), ClawHub skill store (community marketplace), Canvas/A2UI (rich interactive responses). MCP support via mcporter bridge (Model Context Protocol → internal tool calls).
- **nanobot**: Native MCP support in `agent/tools/mcp.py` — both stdio and HTTP transports. Skills defined as YAML files with tool schemas. Subagent spawning via `spawn.py`.

### What DotNetClaw has

No plugin system, no MCP support, no skill loading, no extensibility beyond C# code changes.

### Workflow to fill

```
Startup
  │
  ├── McpToolLoader scans config for MCP servers
  │     ├── stdio transport: spawn process, JSON-RPC over stdin/stdout
  │     └── HTTP transport: HTTP POST to MCP endpoint
  │     │
  │     ▼
  │   Discovers tools → wraps as AITool instances
  │
  ├── SkillsLoader scans mind/skills/*/SKILL.md
  │     ├── Reads frontmatter for tool metadata
  │     └── Body becomes prompt template
  │     │
  │     ▼
  │   Creates AITool wrappers for each skill
  │
  └── ToolRegistry merges:
        ├── Built-in tools (ExecTool, FileSystemTool, WebSearchTool, MemoryTool)
        ├── MCP tools (from McpToolLoader)
        └── Skill tools (from SkillsLoader)
        │
        ▼
      All passed to copilotClient.AsAIAgent(..., tools: mergedTools, ...)
```

### Pseudo code

```csharp
// ── McpToolLoader.cs (new file) ────────────────────────────────────────
// Loads MCP servers and wraps their tools as MAF AITool instances
// NuGet: ModelContextProtocol (official C# SDK)

using ModelContextProtocol;

public sealed class McpToolLoader
{
    public async Task<List<AITool>> LoadAsync(IConfiguration config, CancellationToken ct = default)
    {
        var tools = new List<AITool>();
        var servers = config.GetSection("Mcp:Servers").Get<List<McpServerConfig>>() ?? [];

        foreach (var server in servers)
        {
            IMcpClient client = server.Transport switch
            {
                "stdio" => await McpClient.ConnectStdioAsync(new StdioClientTransportOptions
                {
                    Command = server.Command,
                    Arguments = server.Args,
                }),
                "http" => await McpClient.ConnectHttpAsync(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(server.Url!),
                }),
                _ => throw new ArgumentException($"Unknown MCP transport: {server.Transport}")
            };

            // ListToolsAsync returns MCP tool descriptors
            var mcpTools = await client.ListToolsAsync(ct);
            foreach (var mcpTool in mcpTools)
            {
                // Wrap each MCP tool as an AITool via adapter
                tools.Add(new McpAIToolAdapter(client, mcpTool));
            }
        }

        return tools;
    }
}

public record McpServerConfig
{
    public string Name      { get; init; } = "";
    public string Transport { get; init; } = "stdio";   // "stdio" or "http"
    public string? Command  { get; init; }               // for stdio
    public string[]? Args   { get; init; }               // for stdio
    public string? Url      { get; init; }               // for http
}

// ── McpAIToolAdapter.cs (new file) ─────────────────────────────────────
// Bridges MCP tool → MAF AITool
public sealed class McpAIToolAdapter : AITool
{
    private readonly IMcpClient _client;
    private readonly McpTool _mcpTool;

    public McpAIToolAdapter(IMcpClient client, McpTool mcpTool)
    {
        _client = client;
        _mcpTool = mcpTool;
    }

    // MAF calls this when the agent wants to invoke the tool
    public override async Task<object?> InvokeAsync(
        IDictionary<string, object?> arguments, CancellationToken ct = default)
    {
        var result = await _client.CallToolAsync(_mcpTool.Name, arguments, ct);
        return result.Content.FirstOrDefault()?.Text ?? "";
    }
}

// ── SkillsLoader.cs (new file) ─────────────────────────────────────────
// Loads skill definitions from mind/skills/*/SKILL.md
public sealed class SkillsLoader
{
    public List<AITool> LoadSkills(string mindRoot)
    {
        var skillsDir = Path.Combine(mindRoot, "skills");
        if (!Directory.Exists(skillsDir)) return [];

        var tools = new List<AITool>();
        foreach (var dir in Directory.GetDirectories(skillsDir))
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile)) continue;

            var content = File.ReadAllText(skillFile);
            var (meta, body) = ParseFrontmatter(content);

            // Create a prompt-template tool:
            // When the agent calls this tool, the body is used as a prompt template
            // with the user's input interpolated
            var skillName    = meta.GetValueOrDefault("name", Path.GetFileName(dir));
            var description  = meta.GetValueOrDefault("description", $"Skill: {skillName}");

            tools.Add(AIFunctionFactory.Create(
                (string input) => $"[Skill: {skillName}]\n{body.Replace("{{input}}", input)}",
                skillName,
                description));
        }

        return tools;
    }

    private static (Dictionary<string, string> Meta, string Body) ParseFrontmatter(string content)
    {
        var meta = new Dictionary<string, string>();
        if (!content.StartsWith("---")) return (meta, content);

        var end = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (end < 0) return (meta, content);

        var frontmatter = content[3..end].Trim();
        foreach (var line in frontmatter.Split('\n'))
        {
            var colonIdx = line.IndexOf(':');
            if (colonIdx > 0)
                meta[line[..colonIdx].Trim()] = line[(colonIdx + 1)..].Trim();
        }

        return (meta, content[(end + 3)..].TrimStart());
    }
}

// ── ToolRegistry.cs (new file) ─────────────────────────────────────────
// Merges all tool sources into a single list for the agent
public sealed class ToolRegistry
{
    private readonly List<AITool> _tools = [];

    public void AddBuiltIn(params AITool[] tools) => _tools.AddRange(tools);
    public void AddMcpTools(IEnumerable<AITool> tools) => _tools.AddRange(tools);
    public void AddSkills(IEnumerable<AITool> tools) => _tools.AddRange(tools);

    public IReadOnlyList<AITool> All => _tools.AsReadOnly();
}

// ── Program.cs — merge all tool sources ────────────────────────────────
var registry = new ToolRegistry();

// Built-in tools
registry.AddBuiltIn(
    AIFunctionFactory.Create(execTool.RunAsync),
    AIFunctionFactory.Create(fsTool.ReadFileAsync),
    AIFunctionFactory.Create(fsTool.WriteFileAsync),
    AIFunctionFactory.Create(fsTool.ListDirAsync),
    AIFunctionFactory.Create(webTool.FetchAsync),
    AIFunctionFactory.Create(memTool.SaveFactAsync),
    AIFunctionFactory.Create(memTool.RecallFactsAsync));

// MCP tools (async load at startup)
var mcpLoader = new McpToolLoader();
var mcpTools = await mcpLoader.LoadAsync(builder.Configuration, default);
registry.AddMcpTools(mcpTools);

// Skills from mind/skills/
var skillsLoader = new SkillsLoader();
var skills = skillsLoader.LoadSkills(mind.MindRoot);
registry.AddSkills(skills);

// Pass merged tools to agent
return copilotClient.AsAIAgent(
    ownsClient: true,
    id: "dotnetclaw",
    name: "DotNetClaw",
    description: "Personal AI assistant on WhatsApp and Slack",
    tools: registry.All.ToList(),
    instructions: systemMessage);
```

**appsettings.json addition for MCP servers:**
```json
{
  "Mcp": {
    "Servers": [
      {
        "Name": "filesystem",
        "Transport": "stdio",
        "Command": "npx",
        "Args": ["-y", "@modelcontextprotocol/server-filesystem", "/workspace"]
      },
      {
        "Name": "web-search",
        "Transport": "http",
        "Url": "http://localhost:3001/mcp"
      }
    ]
  }
}
```

**Files to add**: `McpToolLoader.cs`, `McpAIToolAdapter.cs`, `SkillsLoader.cs`, `ToolRegistry.cs`
**Files to change**: `Program.cs` (merge all tool sources), `appsettings.json` (add Mcp section)
**NuGet packages**: `ModelContextProtocol` (official C# MCP SDK)

---

## Summary — Recommended Implementation Order

| Priority | Gap | Effort | Impact |
|---|---|---|---|
| 1 | **Gap 3 — Built-in Tools** | Small | Critical — agent is useless without tools |
| 2 | **Gap 4 — Session Persistence** | Small | Critical — sessions lost on restart |
| 3 | **Gap 6 — Persistent Memory** | Small | Medium — completes the mind/ contract |
| 4 | **Gap 5 — Security** | Medium | High — WhatsApp is wide open |
| 5 | **Gap 2 — Multi-Provider** | Medium | High — removes vendor lock-in |
| 6 | **Gap 8 — Observability** | Small | Medium — typing indicators are quick wins |
| 7 | **Gap 1 — Channels** | Medium per channel | High — 2 vs 22 channels |
| 8 | **Gap 10 — Extensibility** | Large | High — enables ecosystem growth |
| 9 | **Gap 7 — Automation** | Medium | Medium — cron + webhooks |
| 10 | **Gap 9 — Deployment** | Medium | Low — Aspire covers local dev |

Start with Gaps 3 + 4 (tools + persistence) — they're the smallest changes with the highest impact. The agent goes from "chat-only, volatile" to "can act on the world, remembers conversations."
