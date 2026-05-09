# Performance Improvement Plan

## Root Causes

Performance bottlenecks exist at three layers:

### 1. LLM Round Trips (dominant cost)
Both tool-search and code-mode require multiple sequential tool calls, each a separate LLM completion + network round trip:
- **tool-search**: `search` → `call_tool` = 2 turns minimum
- **code-mode**: `search` → `get_schema` → `get_execute_syntax` → `execute` = 4 turns minimum

Each turn blocks on the LLM reasoning cycle before the next call is issued.

### 2. Python Subprocess Spawn per `execute` (code-mode specific)
`LocalConstrainedRunner.ExecutePythonLocallyAsync` creates a new `python3` process on every `execute` call. Python interpreter startup + stdlib imports (`json`, `traceback`, `urllib`, etc.) alone cost 150–400 ms per invocation, before any user code runs.

### 3. Per-Query Re-tokenization in `WeightedToolSearcher` (minor)
`WeightedToolSearcher.Score` re-tokenizes every tool's `Name`, `Description`, and `Tags`, and lowercases the full `InputJsonSchema` on **every** search call. Array `.Contains` is also O(n) instead of O(1) `HashSet` lookups. Minor at 38 tools but wasted CPU that grows linearly with catalog size.

---

## FastMCP Transform Conformance Check

This section maps each proposal to FastMCP transform intent for:
- Tool Search: hide full catalog and discover on demand via `search_tools` + `call_tool`
- Code Mode: default staged discovery (`search` -> `get_schema` -> `execute`) with optional two-stage or single-stage variants

Conformance status:
- **Aligned:** P2, P5, P7
- **Conditionally aligned:** P1, P3, P6
- **Potentially conflicting unless explicitly opting into single-stage behavior:** P4

---

## Improvement Proposals (Highest to Lowest Priority)

---

### P1 — Use `search(detail="Full")` selectively to enable two-stage discovery ✅ Done
**Priority: HIGH (conditional) | Round trips saved: 1 (code-mode) | Effort: Zero**

`search` already accepts a `detail` parameter supporting `Brief`, `Detailed`, and `Full` fidelity. This can collapse `search -> get_schema` into one turn, but should be **conditional**:
- Prefer two-stage (`search` with inline schema) for smaller/known tool subsets.
- Keep three-stage (`search` then `get_schema`) as default for larger catalogs to avoid schema overfetch.

**Change:** System prompt in `CopilotChatService.cs`, Workflow 8 (Code mode):
```
Instead of:
  a. call search → b. call get_schema → c. call execute

Use:
  a. call search with detail=Full → b. call execute
```

Files: `src/McpServer/Services/CopilotChatService.cs`

---

### P2 — Instruct LLM to batch all tool names into one `get_schema` call ✅ Done
**Priority: CRITICAL | Round trips saved: 0–N | Effort: Zero**

`get_schema` accepts a list of tool names. In practice the LLM often calls it one tool at a time. A prompt instruction to always batch is sufficient:

```
Always pass all required tool names in a single get_schema call rather than calling it once per tool.
```

Files: `src/McpServer/Services/CopilotChatService.cs`

---

### P3 — Add a `find_and_call` combined tool (non-transform-native optimization) ✅ Done
**Priority: MEDIUM (conditional) | Round trips saved: 1 (tool-search happy path) | Effort: Low**

Add a single `[McpServerTool]` that atomically does search → pick top match → invoke. For the common single-action case this halves the number of LLM turns from 2 to 1.

Conformance note:
- FastMCP Tool Search is designed around explicit discovery (`search_tools`) followed by explicit invocation (`call_tool` by exact name).
- A fuzzy combined helper is acceptable as an extension, but it is outside the canonical transform pattern and should remain optional.

```csharp
[McpServerTool, Description(
    "Find the best-matching tool by natural language intent and invoke it in one step. " +
    "Use when you are confident about the action. Returns the tool result directly, " +
    "or a disambiguation list if multiple tools match equally.")]
public static async Task<object?> find_and_call(
    [Description("Natural language description of what to do, e.g. 'search breweries in Seattle'")] string intent,
    [Description("Arguments to pass to the matched tool as a JSON object")] JsonElement arguments,
    [FromServices] MetaTools metaTools,
    [FromServices] IToolSearcher searcher,
    [FromServices] UserContext context,
    CancellationToken ct)
{
    IReadOnlyList<ToolDescriptor> candidates = searcher.Search(intent, 3, context);
    if (candidates.Count == 0)
        return "No matching tool found. Use search to browse available tools.";

    // Exact or single match — invoke directly
    ToolDescriptor best = candidates[0];
    if (candidates.Count == 1 || candidates[0].Name.Equals(intent, StringComparison.OrdinalIgnoreCase))
        return await metaTools.CallToolAsync(best.Name, arguments, context, ct);

    // Ambiguous — return candidates for the LLM to pick
    return candidates.Select(t => new { t.Name, t.Description }).ToArray();
}
```

Files: `src/McpServer/Tools/McpToolHandlers.cs` (or a new handler class)

---

### P4 — Inline a compact tool catalog in the system prompt (single-stage only)
**Priority: LOW (conditional) | Round trips saved: 1 (tool-search) | Effort: Low**

Append a one-line-per-tool catalog to the system prompt at startup. At 38 tools × ~8 words each ≈ 150 extra tokens, the LLM can skip the `search` round trip entirely when the user intent is unambiguous.

Conformance note:
- This conflicts with Tool Search's primary design goal (avoid shipping full catalog context upfront) unless you are intentionally selecting a single-stage strategy for small catalogs.

```csharp
// In BootstrapConsoleReporter / Program.cs startup
string catalog = string.Join("\n", tools.Select(t => $"- {t.Name}: {ShortDescription(t.Description)}"));
// Append to PromptModeSystem in CopilotChatService
```

The prompt section would look like:
```
## Available tools (brief)
- list_breweries: List breweries with optional filters (city, state, type)
- get_brewery: Get a single brewery by ID
- search_breweries: Search breweries by name, city, or postal code
...
```

Files: `src/McpServer/Services/CopilotChatService.cs`, `src/McpServer/Program.cs`

---

### P5 — Pre-compute search index entries at registration time ✅ Done
**Priority: MEDIUM | Round trips saved: 0 (latency reduction) | Effort: Low**

`WeightedToolSearcher.Score` re-tokenizes and re-lowercases stable per-tool data on every query. Move this work to startup by introducing a pre-built index entry:

```csharp
private sealed record ToolSearchEntry(
    ToolDescriptor Tool,
    HashSet<string> NameTokens,         // TextNormalizer.Tokenize(tool.Name) — pre-computed
    HashSet<string> DescriptionTokens,  // TextNormalizer.Tokenize(tool.Description) — pre-computed
    HashSet<string> TagTokens,          // all tags flat-tokenized — pre-computed
    string LowerSchema);                // tool.InputJsonSchema.ToLowerInvariant() — pre-computed
```

`WeightedToolSearcher` builds `IReadOnlyList<ToolSearchEntry>` in its constructor. `Score()` becomes:

```csharp
// O(1) set lookups instead of O(n) array scans
if (entry.NameTokens.Contains(token)) score += NameTokenWeight;
if (entry.DescriptionTokens.Contains(token)) score += DescriptionTokenWeight;
if (entry.TagTokens.Contains(token)) score += TagTokenWeight;
```

Files: `src/McpServer/Search/WeightedToolSearcher.cs`

---

### P6 — Warm Python process pool for `execute`
**Priority: MEDIUM | Round trips saved: 0 (latency reduction ~200–400 ms/call) | Effort: Medium**

Replace the per-call `Process.Start()` in `LocalConstrainedRunner` with a pool of 1–3 pre-warmed Python processes that speak a simple stdin/stdout JSON-RPC line protocol:

```
[host → python stdin]  {"id": "abc", "code": "result = 1 + 1"}
[python → host stdout] {"id": "abc", "ok": true, "finalValue": 2}
```

The Python worker process enters a `while True` loop at startup, reading one JSON line per iteration, `exec()`-ing the code in a fresh `scope = {}`, and writing the result. The C# pool keeps workers alive across calls using `System.Threading.Channels.Channel<PooledWorker>` and replaces crashed workers transparently.

Benefits:
- Eliminates ~200–400 ms Python interpreter startup + import cost per call
- Pool size can be configured (default 2 workers)

Conformance note:
- Code Mode requires sandboxed execution with enforceable limits.
- Any worker pool must preserve per-run isolation semantics (fresh scope, no cross-request state leakage, and limits enforcement) or it violates the sandbox model.

Files: `src/McpServer/CodeMode/LocalConstrainedRunner.cs` + new `PythonWorkerPool.cs`

---

### P7 — Dictionary index for `ToolRegistry` lookups ✅ Done
**Priority: LOW | Round trips saved: 0 (minor latency) | Effort: Low**

`ToolRegistry.FindByName` and `InvokeAsync` both do linear `FirstOrDefault` O(n) scans. At 38 tools this is negligible but becomes noticeable if the catalog grows.

```csharp
private readonly Dictionary<string, ToolDescriptor> _byName;

public ToolRegistry(IReadOnlyList<ToolDescriptor> tools)
{
    _tools = tools;
    _byName = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
}

public ToolDescriptor? FindByName(string name, UserContext context)
{
    if (!_byName.TryGetValue(name, out ToolDescriptor? tool)) return null;
    return tool.IsVisible(context) ? tool : null;
}
```

Files: `src/McpServer/Registry/ToolRegistry.cs`

---

## Summary Table

| # | Proposal | Round trips saved | Latency saved | Code changes | Effort |
|---|---|---|---|---|---|
| P1 ✅ | `search` with `detail=Full` replaces `get_schema` | 1 (code-mode) | — | System prompt only | None |
| P2 ✅ | Batch all names in one `get_schema` call | 0–N | — | System prompt only | None |
| P3 ✅ | `find_and_call` combined tool | 1 (tool-search) | — | New `[McpServerTool]` | Low |
| P4 | Inline catalog in system prompt | 1 (tool-search) | — | Startup + prompt | Low |
| P5 ✅ | Pre-compute search index | 0 | Minor CPU | `WeightedToolSearcher` | Low |
| P6 | Python process pool | 0 | 200–400 ms/call | `LocalConstrainedRunner` | Medium |
| P7 ✅ | Dictionary index in `ToolRegistry` | 0 | Negligible at 38 tools | `ToolRegistry` | Low |

**Recommended execution order (transform-conformant):**
1. P2 (batch `get_schema` calls; always aligned)
2. P5 + P7 (search/registry internal perf improvements)
3. P1 as a conditional prompt strategy (two-stage when result set is small, fallback to three-stage otherwise)
4. P3 as optional extension (do not replace canonical `search_tools` + `call_tool` flow)
5. P6 only with strict sandbox isolation guarantees
6. P4 only when intentionally adopting single-stage discovery for small catalogs

---

## Implementation Phase 1: System Prompt Updates (P1 + P2)

**File:** `src/McpServer/Services/CopilotChatService.cs`

### P1: Workflow 8 — Code mode section update

Current (lines 29–38 of `PromptModeSystem`):
```csharp
### 8 — Code mode (pure Python compute)
Use `execute` only when the task genuinely requires Python logic (e.g. sorting, filtering, math across many results) that cannot be done with a single tool call.
Steps:
  a. Call `get_execute_syntax` to confirm the runner capabilities.
  b. Write pure Python; use the `requests`-compatible HTTP shim for any HTTP calls.
  c. Do not call `search`, `get_schema`, `call_tool`, or `execute` from inside execute code.
  d. Do not generate JavaScript or TypeScript for execute.
```

Replace with:
```csharp
### 8 — Code mode (pure Python compute)
Use `execute` only when the task genuinely requires Python logic (e.g. sorting, filtering, math across many results) that cannot be done with a single tool call.
Steps:
  a. Call `search` with `detail="Full"` to get tool schemas in one step instead of calling `get_schema` separately. Example: search("breweries in Seattle", detail="Full")
  b. Call `get_execute_syntax` to confirm the runner capabilities.
  c. Write pure Python; use the `requests`-compatible HTTP shim for any HTTP calls.
  d. Do not call `search`, `get_schema`, `call_tool`, or `execute` from inside execute code.
  e. Do not generate JavaScript or TypeScript for execute.
```

### P2: Core rules section update

Add to the "Core rules" section (after line 13):
```csharp
- When you need multiple tool schemas before writing code, pass all tool names to one `get_schema` call instead of calling it multiple times.
```

---

## Implementation Phase 2: New Tool + Catalog Injection (P3 + P4)

### P3: Add `find_and_call` to McpToolHandlers

**File:** `src/McpServer/Tools/McpToolHandlers.cs`

Add the following method to the `McpToolHandlers` class (after `call_tool` method, around line 80):

```csharp
/// <summary>
/// Single-step tool discovery and invocation. Searches for a matching tool by intent
/// and immediately invokes it if a single match is found. Returns the tool result directly.
/// If multiple tools match equally, returns a disambiguation list for manual selection.
/// </summary>
[McpServerTool, Description("Find the best-matching tool by natural language intent and invoke it in one step. Use when you are confident about the action. Returns the tool result directly, or a disambiguation list if multiple tools match equally.")]
public static async Task<object?> find_and_call(
    [Description("Natural language description of what to do, e.g. 'search breweries in Seattle'")] string intent,
    [Description("Arguments to pass to the matched tool as a JSON object")] JsonElement arguments,
    [FromServices] IToolSearcher searcher,
    [FromServices] MetaTools metaTools,
    [FromServices] UserContext context,
    [FromServices] ILoggerFactory loggerFactory,
    CancellationToken ct)
{
    IReadOnlyList<ToolDescriptor> candidates = searcher.Search(intent, 3, context);
    
    if (candidates.Count == 0)
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
        logger.LogInformation("MCP handler {HandlerName}: no matching tools found for intent '{Intent}'.", nameof(find_and_call), intent);
        return "No matching tool found. Use search to browse available tools.";
    }

    ToolDescriptor best = candidates[0];
    
    // Exact match or single result — invoke directly
    if (candidates.Count == 1 || best.Name.Equals(intent, StringComparison.OrdinalIgnoreCase))
    {
        ILogger logger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
        logger.LogInformation("MCP handler {HandlerName}: invoking tool '{ToolName}' for intent '{Intent}'.", nameof(find_and_call), best.Name, intent);
        
        Activity.Current?.SetTag("mcp.handler.name", nameof(find_and_call));
        Activity.Current?.SetTag("mcp.handler.matched.tool", best.Name);
        
        return await metaTools.CallToolAsync(best.Name, arguments, context, ct);
    }

    // Ambiguous — return top candidates for the LLM to pick
    ILogger ambigLogger = loggerFactory.CreateLogger(typeof(McpToolHandlers));
    ambigLogger.LogInformation("MCP handler {HandlerName}: ambiguous intent '{Intent}', returning {CandidateCount} candidates.", nameof(find_and_call), intent, candidates.Count);
    
    Activity.Current?.SetTag("mcp.handler.name", nameof(find_and_call));
    Activity.Current?.SetTag("mcp.handler.candidates.count", candidates.Count);
    
    return candidates.Select(t => new { t.Name, t.Description }).ToArray();
}
```

### P4: Inject catalog into system prompt at startup

**File:** `src/McpServer/Program.cs`

After the line `BootstrapConsoleReporter.WriteReport(openApiSources, tools);` (around line ~135), add:

```csharp
// Build compact tool catalog for system prompt
string toolCatalog = string.Join("\n", tools
    .Select(t => $"- {t.Name}: {BootstrapConsoleReporter.ToShortDescription(t.Description)}")
    .OrderBy(line => line));

logger.LogInformation("[Bootstrap] Tool catalog injected into system prompt ({ToolCount} tools, ~{Tokens} tokens).",
    tools.Count,
    Math.Max(1, (int)Math.Ceiling(toolCatalog.Length / 4d)));
```

Then modify the `CopilotChatService` instantiation to accept the catalog. **File:** `src/McpServer/Services/CopilotChatService.cs`

Update the constructor signature (around line 62):
```csharp
public CopilotChatService(IConfiguration configuration, ILogger<CopilotChatService> logger, string? toolCatalog = null)
{
    model = configuration["Copilot:Model"] ?? "gpt-5";
    gitHubToken = configuration["Copilot:GitHubToken"];
    mcpEndpoint = configuration["Mcp:Endpoint"] ?? "http://localhost:5100/mcp";
    mcpServerName = configuration["Copilot:McpServerName"] ?? "mcp-experiments";
    this.logger = logger;

    // ... existing code ...

    // Append catalog to system prompt if provided
    string systemPrompt = PromptModeSystem;
    if (!string.IsNullOrWhiteSpace(toolCatalog))
    {
        systemPrompt += "\n## Available tools (brief)\n" + toolCatalog;
    }
    this.systemPrompt = systemPrompt;
}

private readonly string systemPrompt;  // Add this field
```

Update `BuildMcpServerConfig` to use `systemPrompt` field instead of `PromptModeSystem` constant.

Update DI registration in `Program.cs`:
```csharp
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var logger = sp.GetRequiredService<ILogger<CopilotChatService>>();
    var toolCatalog = ... // retrieve from app context or builder
    return new CopilotChatService(config, logger, toolCatalog);
});
```

---

## Implementation Phase 3: Search/Registry Cleanup (P5 + P7)

### P5: Pre-computed search index in WeightedToolSearcher

**File:** `src/McpServer/Search/WeightedToolSearcher.cs`

Add new record at the top of the file (after `namespace` declaration):
```csharp
private sealed record ToolSearchEntry(
    ToolDescriptor Tool,
    HashSet<string> NameTokens,
    HashSet<string> DescriptionTokens,
    HashSet<string> TagTokens,
    string LowerSchema);
```

Update the `WeightedToolSearcher` class:

```csharp
public sealed class WeightedToolSearcher(IToolRegistry registry) : IToolSearcher
{
    private const int ExactNameWeight = 100;
    private const int NameTokenWeight = 30;
    private const int DescriptionTokenWeight = 10;
    private const int ParameterNameTokenWeight = 8;
    private const int ParameterDescriptionTokenWeight = 4;
    private const int TagTokenWeight = 3;

    private readonly IReadOnlyList<ToolSearchEntry> _searchIndex;

    public WeightedToolSearcher(IToolRegistry registry)
    {
        // Pre-compute search entries at construction time
        var tools = registry.GetVisibleTools(new UserContext()); // or pass a default context
        var entries = new List<ToolSearchEntry>(tools.Count);
        
        foreach (var tool in tools)
        {
            entries.Add(new ToolSearchEntry(
                tool,
                new HashSet<string>(TextNormalizer.Tokenize(tool.Name), StringComparer.Ordinal),
                new HashSet<string>(TextNormalizer.Tokenize(tool.Description), StringComparer.Ordinal),
                new HashSet<string>(tool.Tags.SelectMany(TextNormalizer.Tokenize), StringComparer.Ordinal),
                tool.InputJsonSchema.ToLowerInvariant()
            ));
        }
        
        _searchIndex = entries;
    }

    public IReadOnlyList<ToolDescriptor> Search(string query, int limit, UserContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(context);

        if (limit <= 0) return [];

        string normalizedQuery = query.Trim().ToLowerInvariant();
        string[] queryTokens = TextNormalizer.Tokenize(query);
        IReadOnlyList<ToolDescriptor> candidates = registry.GetVisibleTools(context);

        return candidates
            .Select((tool, index) =>
            {
                var entry = _searchIndex.FirstOrDefault(e => e.Tool.Name == tool.Name);
                return new RankedTool(tool, entry is not null ? Score(entry, normalizedQuery, queryTokens) : 0, index);
            })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Index)
            .Take(limit)
            .Select(item => item.Tool)
            .ToArray();
    }

    private static int Score(ToolSearchEntry entry, string normalizedQuery, string[] queryTokens)
    {
        int score = 0;
        string lowerName = entry.Tool.Name.ToLowerInvariant();

        if (lowerName.Equals(normalizedQuery, StringComparison.Ordinal)) score += ExactNameWeight;

        foreach (string token in queryTokens)
        {
            // O(1) HashSet lookups instead of O(n) array scans
            if (entry.NameTokens.Contains(token, StringComparer.Ordinal)) score += NameTokenWeight;
            if (entry.DescriptionTokens.Contains(token, StringComparer.Ordinal)) score += DescriptionTokenWeight;
            if (entry.TagTokens.Contains(token, StringComparer.Ordinal)) score += TagTokenWeight;
            score += ScoreSchemaFields(entry.LowerSchema, token);
        }
        return score;
    }

    private static int ScoreSchemaFields(string lowerSchema, string token)
    {
        if (string.IsNullOrWhiteSpace(lowerSchema)) return 0;
        int score = 0;
        if (lowerSchema.Contains($"\"{token}\"", StringComparison.Ordinal)) score += ParameterNameTokenWeight;
        if (lowerSchema.Contains($":\"{token}", StringComparison.Ordinal) ||
            lowerSchema.Contains($" {token}", StringComparison.Ordinal)) score += ParameterDescriptionTokenWeight;
        return score;
    }

    private sealed record RankedTool(ToolDescriptor Tool, int Score, int Index);
}
```

### P7: Dictionary index in ToolRegistry

**File:** `src/McpServer/Registry/ToolRegistry.cs`

Update the class:

```csharp
public sealed class ToolRegistry(IReadOnlyList<ToolDescriptor> tools) : IToolRegistry
{
    private readonly IReadOnlyList<ToolDescriptor> _tools = tools;
    private readonly Dictionary<string, ToolDescriptor> _byName = 
        tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ToolDescriptor> GetVisibleTools(UserContext context)
        => _tools.Where(tool => tool.IsVisible(context)).ToArray();

    public ToolDescriptor? FindByName(string name, UserContext context)
    {
        if (!_byName.TryGetValue(name, out ToolDescriptor? tool)) return null;
        return tool.IsVisible(context) ? tool : null;
    }

    public async Task<object?> InvokeAsync(string name, JsonElement arguments, UserContext context, CancellationToken ct)
    {
        if (!_byName.TryGetValue(name, out ToolDescriptor? tool))
            throw new ToolNotFoundException(name);
        
        if (!tool.IsVisible(context)) throw new ToolAccessDeniedException(name);

        return await tool.Handler(arguments, ct);
    }
}
```

---

## Verification Steps

After each phase, run:

```bash
# Build
dotnet build McpExperiments.slnx

# Run unit tests
dotnet test McpExperiments.slnx

# Deploy to Docker/Kubernetes
docker compose build
kubectl rollout restart -n mcp-experiments deploy/mcp-server
```

Monitor logs for any errors and verify tool performance improvements via wall-clock time measurements.
