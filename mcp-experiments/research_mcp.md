# Research: `elusznik/mcp-server-code-execution-mode` vs. this repository

A deep-dive comparison of approach, features, and algorithms — language-agnostic.

---

## 1. The upstream project at a glance

**Repository:** [elusznik/mcp-server-code-execution-mode](https://github.com/elusznik/mcp-server-code-execution-mode/) (Python, GPL-3.0).

**Tagline:** *"Zero-Context Discovery for 100+ MCP Tools."* Implements Anthropic's **Code Execution with MCP** pattern with a hardened, rootless container sandbox.

### 1.1 What it actually is

A **bridge / aggregator** that sits between an MCP client (e.g., Claude Desktop) and a fleet of *other* MCP servers discovered from the host filesystem. Instead of forwarding every backend tool's schema into the client's context, it presents **a single tool: `run_python`**.

The LLM solves tasks by **writing Python** that:
1. discovers available servers/tools at runtime,
2. fetches schemas only for what it needs,
3. calls them via auto-generated `mcp_<alias>` proxies,
4. and returns the final value.

### 1.2 Architecture (3 layers)

```
MCP Client  ── stdio JSON-RPC ──▶  Bridge (Python)  ── subprocess + JSON frames ──▶  Rootless container (podman/docker)
                                       │                                                       │
                                       │                                                       │ runs entrypoint.py
                                       │                                                       │ exposes mcp.runtime helpers
                                       └── PersistentMCPClient pool ◀── JSON-framed RPC ───────┘
                                              │
                                              ▼
                                       Real backend MCP servers (stdio)
```

Key components:
- **`mcp_server_code_execution_mode.py`** — exposes `run_python`, validates input, owns container & client lifecycles.
- **`PersistentMCPClient`** — keeps stdio sessions to discovered backend MCP servers warm across invocations.
- **`SandboxInvocation`** — per-call: builds `/ipc` tmpfs, writes generated `entrypoint.py`, sets `MCP_AVAILABLE_SERVERS`, services RPC from the sandbox via `handle_rpc`.
- **Generated entrypoint** inside the container — rewires stdio into JSON frames, supports top-level `await`, exposes `mcp.runtime` helpers and `mcp_<alias>` proxies.

### 1.3 Discovery model — *"zero-context"*

The system prompt only advertises a handful of helper functions (~200 tokens), never tool schemas:

| Helper (callable from sandbox code) | Purpose |
|---|---|
| `discovered_servers()` / `list_servers_sync()` | Enumerate available MCP servers |
| `query_tool_docs(server, tool=…, detail="full")` | Hydrate full or summary schema for one server |
| `search_tool_docs("keyword", limit=…)` | **Fuzzy search** across servers' tool docs |
| `describe_server(name)` / `list_loaded_server_metadata()` | Inspect loaded servers (incl. configured `cwd`) |
| `capability_summary()` | One-paragraph self-description |

The model writes a script, calls these helpers inline, then dispatches RPCs — all **within one `run_python` round-trip**.

### 1.4 Server discovery (host-side)

Auto-scans **12+ known config locations** for backend MCP server definitions, with a strict precedence order:
- `~/MCPs/`, `~/.config/mcp/servers/`, `./mcp-servers/`, `./.vscode/mcp.json`,
- `~/.claude.json`, `~/.cursor/mcp.json`, `~/.opencode.json`, `~/.codeium/windsurf/mcp_config.json`,
- macOS Claude Desktop / Claude Code / VS Code global, Linux equivalents.

Recursion protection: skips any config that would re-launch the bridge itself unless `MCP_BRIDGE_ALLOW_SELF_SERVER=1`.

### 1.5 Sandbox & security posture

Per-execution **fresh container**, rootless by default:

| Constraint | Flag | Effect |
|---|---|---|
| Network | `--network none` | No external/internal network |
| Filesystem | `--read-only` + tmpfs `/workspace`, `/ipc` | Immutable rootfs |
| Capabilities | `--cap-drop ALL` | No syscalls beyond user-mode |
| Privileges | `--security-opt no-new-privileges` | No setuid escalation |
| User | `65534:65534` | nobody / nogroup |
| Memory | `--memory 512m` | Configurable |
| PIDs | `--pids-limit 128` | Fork bomb cap |
| Image | `python:3.14-slim` | Configurable via `MCP_BRIDGE_IMAGE` |

For Podman: bridge automatically issues `podman machine set --rootful --now --volume <state_dir>` and probes the VM for share availability on older Podman builds.

### 1.6 Performance / lifecycle features

- **Persistent sessions**: variables, imports, functions defined in one `run_python` call survive into the next (state retention across invocations).
- **Persistent backend MCP clients**: the bridge keeps stdio handshakes warm.
- **Lazy runtime detection**: bridge starts even if Podman/Docker isn't ready; checks at first execute.
- **Idle shutdown**: Podman machine auto-stops after `MCP_BRIDGE_RUNTIME_IDLE_TIMEOUT` (default 300s).

### 1.7 Output formatting

- Default **compact**: plain text + minimal `structuredContent` (drops empty fields).
- Optional **TOON** (`MCP_BRIDGE_OUTPUT_MODE=toon`): [Token-Oriented Object Notation](https://github.com/toon-format/toon) for deterministic tokenization downstream.
- Falls back to pretty JSON if TOON encoder unavailable.

### 1.8 Persistent memory

A JSON-backed memory store (`save_memory`, `load_memory`, `update_memory`, `list_memories`, `memory_exists`) persisted to `/projects/memory/` (host: `~/MCPs/user_tools/memory/`) — **survives across container restarts and sessions**. Effectively gives the agent its own scratchpad.

### 1.9 The `run_python` invocation contract

```jsonc
{
  "code": "print(await mcp_serena.search(query='latest AI papers'))",
  "servers": ["serena", "filesystem"],   // explicit opt-in load list
  "timeout": 30
}
```

`servers` controls **which proxies are generated** for the sandbox; if you omit it, discovery still works but RPC to unloaded servers returns `Server '<name>' is not available`.

---

## 2. This repository's approach (`mcp-experiments`)

A .NET 10 MCP server combining three patterns explicitly: **tool-search**, **code-mode**, and **OpenSandbox** isolation.

### 2.1 Surface area exposed to the LLM

Unlike the upstream's *one* tool, this repo exposes **two parallel meta-tool families** plus a pinned `status`:

| Family | Tools | Workflow |
|---|---|---|
| **Tool-search** | `search_tools(query, limit)`, `call_tool(name, arguments)` | Multi-turn: search → invoke directly |
| **Code-mode** | `search`, `get_schema`, `get_execute_syntax`, `execute(code)` | Staged: discover → schema → syntax → run |
| **Pinned** | `status` | Always visible health check |

Real backend tools are **not registered** with the MCP server. They live in an internal `IToolRegistry` and are surfaced only through these meta-tools.

### 2.2 Backend tool catalog

Tools come from **OpenAPI documents**, not other MCP servers:
- `OpenApiToolCatalogBuilder.BuildAsync` loads OpenAPI YAML/JSON sources (e.g., [`contracts/openbrewerydb.v1.openapi.yaml`](contracts/openbrewerydb.v1.openapi.yaml), Petstore).
- Each operation becomes a `ToolDescriptor` with handler, JSON schema, tags.
- Allowed base URLs are extracted from the OpenAPI `servers` block and pushed into the runner as a **URL allowlist**.

### 2.3 Discovery algorithm (`Search` / `GetSchema`)

In [src/McpServer/CodeMode/DiscoveryTools.cs](src/McpServer/CodeMode/DiscoveryTools.cs):
- Backed by a **`WeightedToolSearcher`** (BM25-ish weighted ranking over name/description/tags).
- Three explicit detail levels:
  - `Brief` (0): name only,
  - `Detailed` (1): compact markdown of params/types/required flags,
  - `Full` (2): raw JSON schema.
- Optional `tags` filter and `limit`.
- `GetSchema` reports **missing names** explicitly.

The upstream's `search_tool_docs` is conceptually similar (fuzzy + limit), but its detail axis is binary (`detail="full"` or summary) rather than three-tier.

### 2.4 Execution model

[src/McpServer/CodeMode/ExecuteTool.cs](src/McpServer/CodeMode/ExecuteTool.cs) delegates to an `ISandboxRunner`. Two implementations:

#### a) `LocalConstrainedRunner` (default)
[src/McpServer/CodeMode/Local/LocalConstrainedRunner.cs](src/McpServer/CodeMode/Local/LocalConstrainedRunner.cs)
- Spawns Python locally via subprocess, base64-encoding the user code into a wrapper script.
- Injects `BASE_URL` and a hand-rolled `requests`-compatible shim built on `urllib`.
- **Static URL allowlist guard** — regex-scans submitted code and rejects any absolute HTTP(S) URL not under a configured OpenAPI base.
- Returns `result` variable or stdout.
- **No tool-bridge access at all** — the sandbox cannot call back into the registry.

#### b) `OpenSandboxRunner`
[src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs)
- Uses [Alibaba OpenSandbox](https://github.com/alibaba/OpenSandbox) (`Alibaba.OpenSandbox` NuGet) — a dedicated remote sandbox service.
- Per-call: `Sandbox.CreateAsync` → `sandbox.Commands.RunAsync` → `KillAsync`.
- **No reuse, no persistent state** between executions.
- Ephemeral by design: see repo memory *"Sandbox is ephemeral: create one per execute call, kill it when done. Never reuse across requests."*
- Retry with exponential backoff: 3 attempts on creation, 2 on command execution ([src/McpServer/CodeMode/OpenSandbox/RetryHelper.cs](src/McpServer/CodeMode/OpenSandbox/RetryHelper.cs)).
- Injects the same `requests` shim into Python via `BuildPythonCommand`.

### 2.5 Code-mode isolation guard

[src/McpServer/CodeMode/SandboxCodeGuard.cs](src/McpServer/CodeMode/SandboxCodeGuard.cs) — a regex that **statically rejects** code containing calls to any meta-tool name (`SearchTools|CallTool|Search|GetSchema|Execute`). Prevents recursion *inside generated code*.

The upstream applies a different protection layer: it filters host-side configs that would re-launch the bridge itself, but inside the sandbox it actively *encourages* calling discovery helpers from generated Python.

### 2.6 Tool-search-tool pair (alternate, non-code path)

[src/McpServer/ToolSearch/MetaTools.cs](src/McpServer/ToolSearch/MetaTools.cs) — `SearchTools` + `CallToolAsync`. Plain "search → call" loop, schemas inlined per result via `ToolDefinition`. Matches Speakeasy's "Dynamic Toolsets" 3-step flow that the upstream explicitly criticizes.

### 2.7 Transport & hosting

Streamable HTTP MCP via ASP.NET Core (`/mcp` endpoint), Aspire AppHost orchestrates `mcp-server`, `test-web`, and an `opensandbox-server` container. Upstream is **stdio-only**.

---

## 3. Side-by-side comparison

### 3.1 Conceptual surface

| Aspect | upstream `mcp-server-code-execution-mode` | this repo (`mcp-experiments`) |
|---|---|---|
| Tools exposed to client | **1**: `run_python` | **6+**: `status`, `search_tools`, `call_tool`, `search`, `get_schema`, `get_execute_syntax`, `execute` |
| Discovery happens... | *Inside* generated code via `mcp.runtime` helpers | *Before* code via dedicated MCP tool calls (`search`, `get_schema`) |
| Backends | Other MCP servers (any stdio) | OpenAPI documents → in-process handlers |
| LLM round-trips per task | **1** (search + invoke + compute in one script) | **3–4** (search → get_schema → get_execute_syntax → execute) |
| Coexistence with non-code clients | No — code-only | Yes — `search_tools`/`call_tool` works for clients that don't want code mode |

### 3.2 Token / context efficiency

| Aspect | upstream | this repo |
|---|---|---|
| Initial system prompt | ~200 tokens (helpers only) | Larger — 6 tools + each tool's `[Description]` |
| Schema flow | On-demand, in-band (via runtime helpers) | On-demand, out-of-band (separate MCP tool call) |
| Explicit detail axis | `detail="full"` vs. summary | Three-tier `Brief` / `Detailed` / `Full` |
| Output format | Compact text or TOON, drops empty fields | Default JSON (single `FinalValue`) |

The upstream is **strictly more token-efficient** at idle: one tool name vs. seven. This repo trades some token cost for backwards-compatible non-code workflows and a strongly-typed staged API.

### 3.3 Sandbox & security

| Aspect | upstream | this repo |
|---|---|---|
| Isolation | Rootless podman/docker, fresh container per call, full hardening (no net, ro-fs, cap-drop, no-new-privileges, UID 65534, mem/PID/CPU caps) | **LocalConstrainedRunner**: subprocess only, **no kernel-level isolation**. **OpenSandboxRunner**: delegates to Alibaba OpenSandbox service. |
| Network policy | Hard `--network none` | Static **regex-based URL allowlist** in user code (LocalRunner only); OpenSandbox controls its own egress |
| Egress allowlist | None needed (no net) | Required to permit OpenAPI bases |
| Tool-call guard | Encouraged (helpers are the API) | Forbidden via regex (code-mode is *isolated* from registry) |
| Persistence | **Variables persist across calls** (warm container) | **Stateless** — fresh sandbox per execute |
| Container management | Bridge owns lifecycle | OpenSandbox is external; AppHost orchestrates locally; LocalRunner has no container |

The upstream's "rootless container per call with kernel-level isolation" is a stronger threat model than this repo's `LocalConstrainedRunner`, which relies on regex guards in *userspace* and runs in the same OS user as the server. The OpenSandbox runner closes that gap by delegating to a hardened external service.

### 3.4 Tool composition algorithm

| Aspect | upstream | this repo |
|---|---|---|
| Composition primitive | Generated Python proxies (`mcp_<alias>.tool(...)`) | None inside `execute` — code is *isolated* from tool calls |
| Cross-tool orchestration | Free-form Python (loops, retries, conditionals, data joins) | Must happen *outside* `execute` via repeated `call_tool`, OR by re-calling APIs directly via the `requests` shim against allowed base URLs |
| Top-level await | Yes (entrypoint supports it) | N/A — code is sync Python (or async via standard means) |
| Multi-server orchestration in one script | First-class | Not supported — single OpenAPI base per execute |

This is the **biggest divergence**. Upstream's whole point is *"the agent writes Python that fans out across MCP servers in one round-trip"*. This repo's code-mode deliberately **disallows** calling tools from inside `execute` (`SandboxCodeGuard`), turning it into a **pure compute / HTTP-fetch** runner instead of a tool-orchestration runner. Multi-tool composition reverts to the chatty `search_tools` → `call_tool` loop.

### 3.5 Discovery scope

| Aspect | upstream | this repo |
|---|---|---|
| Backend source | Filesystem scan of 12+ MCP config locations | OpenAPI source list configured at boot |
| Dynamic add | New MCP servers picked up via config refresh | Static — tools loaded once at startup from OpenAPI |
| Recursive-self guard | Yes (skips bridge re-launch entries) | N/A (no auto-discovery) |
| Search algorithm | Fuzzy match across cached tool docs | Weighted BM25-ish ranker over name+description+tags |

### 3.6 Persistent memory & state

| upstream | this repo |
|---|---|
| Built-in `save_memory`/`load_memory` API persisted to host disk; survives restarts | None — each `execute` is stateless; chat-side has `ChatTurnMetrics` token accounting only |

### 3.7 Output formats

| upstream | this repo |
|---|---|
| Compact text (default), TOON, JSON fallback; drops empty fields | JSON `ExecuteResponse { FinalValue }` only |

### 3.8 Operational features

| Feature | upstream | this repo |
|---|---|---|
| Transport | stdio | streamable HTTP |
| Multi-client | Per-client process | Single shared HTTP endpoint |
| Aspire / orchestration | No | Yes (AppHost) |
| Container runtime auto-detect | podman/docker auto | Docker via OpenSandbox config |
| Lazy runtime checks | Yes | Yes (sandbox created lazily) |
| Retry with backoff | Implicit (warm clients) | Explicit `RetryHelper` for OpenSandbox |
| Self-recursion protection | Config-level | Code-level regex (`SandboxCodeGuard`) |

---

## 4. Algorithmic differences worth highlighting

1. **Discovery placement.** Upstream pushes discovery *into the sandbox runtime* (helpers callable from generated code). This repo keeps discovery *outside* the sandbox as separate MCP tools. The upstream design enables **discovery + decision + execution in a single LLM turn**; this repo's design enforces **a deterministic staged pipeline** at the cost of extra round-trips.

2. **Tool invocation model.** Upstream: code → JSON RPC frames over container stdio → host bridge → backend MCP servers. This repo: code → direct HTTP via `requests` shim → external OpenAPI; or `call_tool` → in-process registry handler. Upstream **proxies arbitrary MCP servers**; this repo **wraps OpenAPI directly**.

3. **State retention.** Upstream's container persists between calls (warm Python interpreter, retained variables/imports). This repo's runners are stateless per call (especially OpenSandbox, which is created/killed each execute).

4. **Security boundary.** Upstream draws the boundary at the **kernel** (rootless containers). This repo draws it at **userspace regex guards** (LocalRunner) or **delegated remote sandbox** (OpenSandbox).

5. **Token-bloat fight.** Upstream's claim is *constant* prompt overhead (~200 tokens) regardless of catalog size. This repo's prompt scales with the number of meta-tool descriptions (still small, but not constant), and individual tool schemas only flow on demand via `get_schema`.

6. **Composition vs. isolation.** Upstream optimizes for **maximum composition** inside the sandbox (multiple MCP servers in one script). This repo deliberately **forbids in-sandbox tool calls** to prevent recursion and meta-tool abuse — a different threat-model trade-off.

---

## 5. Lessons & potential improvements for this repo

Ideas worth considering, distilled from the upstream design:

| Idea | What it would take here |
|---|---|
| **Collapse the staged pipeline into one tool** for code-capable clients | Add an optional `run_code(code, sources?)` that internally bypasses `get_execute_syntax`/`get_schema` round-trips, exposing a runtime helper API inside the sandbox |
| **Make discovery callable from inside `execute`** | Add a sandbox-side `runtime.search(...)` / `runtime.call(name, args)` API that proxies *outwards* to the registry over stdio frames; remove the blanket regex guard in favor of allowlisted RPC |
| **Persistent sandbox sessions** | Reuse an OpenSandbox instance across calls to retain Python state (saves both latency and token cost when the LLM iterates) |
| **TOON / compact output mode** | Add an output formatter to `ExecuteResponse` that drops empty fields, optionally renders TOON |
| **Persistent memory store** | Add a `save_memory`/`load_memory` pair (file or DB-backed) for cross-turn agent scratchpads |
| **Dynamic server discovery** | Reload OpenAPI sources at runtime (file-watch) so new tools appear without a restart |
| **Stronger local sandbox** | Replace `LocalConstrainedRunner`'s subprocess with a rootless container or seccomp profile if OpenSandbox isn't available |
| **Tag-based search returned by upstream** | Already present here as `tags` filter — actually a *win* for this repo over the upstream's flat fuzzy search |

---

## 6. Where this repo is already ahead

- **Three-tier schema detail** (`Brief` / `Detailed` / `Full`) is more granular than the upstream's binary toggle.
- **Tag-based filtering** in `search` is not present upstream.
- **Streamable HTTP transport** + Aspire orchestration suits multi-client/server deployments better than stdio-only.
- **Strict code-mode isolation** (regex guard) is a useful safety net the upstream doesn't have, even if it constrains composition.
- **Static URL allowlist** derived from OpenAPI servers gives provable egress boundaries the upstream achieves only via `--network none` (a stricter but blunter tool).
- **Dual surface** (`search_tools`/`call_tool` + `search`/`get_schema`/`execute`) lets non-code-capable clients still benefit from the registry.

---

## 7. Summary

The upstream project is a **single-tool, container-isolated Python orchestrator** that pushes discovery *into* the sandbox. Its standout features are kernel-grade isolation, persistent warm sessions, fuzzy discovery from inside generated code, persistent memory, and TOON output — all aimed at **constant ~200-token overhead** regardless of how many MCP servers exist.

This repo is a **multi-tool, staged-discovery, OpenAPI-backed** experiment with two parallel pathways (tool-search vs. code-mode), pluggable runners (Local subprocess vs. OpenSandbox), strong egress allowlisting, and a strict isolation rule that forbids tool calls from inside generated code. It trades some token-efficiency and composition power for a strongly-typed pipeline, dual client compatibility, and a predictable security model.

The two designs are complementary: upstream maximizes **composition inside the sandbox**; this repo maximizes **structure outside it**. The most impactful evolutions for this repo would be (a) optionally exposing a sandbox-side `runtime` helper to recover one-round-trip composition, and (b) allowing persistent sandbox sessions for stateful agentic loops.


---

## 8. Research plan: framework-agnostic MCP core + optional MAF host (best-of-breed)

> **Goal:** Build a **framework-agnostic MCP server** that combines (a) the **zero-context discovery + code-execution** algorithm from `mcp-server-code-execution-mode` (incl. `tool-search`, code-mode staged flow, and a `run_code` Python entrypoint) with (b) this repo's **OpenAPI-driven tool catalog** and **staged schema detail** levels.
>
> **Microsoft Agent Framework (MAF)** is exposed only as **one optional host adapter** alongside others (raw ASP.NET Core, Semantic Kernel, AutoGen, LangChain, plain stdio). The MCP core MUST NOT take a compile-time dependency on MAF, so the project remains portable to other agent frameworks in the future.

### 8.1 Feasibility verdict

**Yes — feasible, and the decoupling is the design's load-bearing constraint.** Concretely:

- MCP itself is a **transport-level protocol**; it has no notion of "agent framework". The current repo already proves this — the server runs as plain ASP.NET Core with no MAF dependency.
- The upstream project's algorithm (in-sandbox discovery helpers, fuzzy search, on-demand schema hydration, generated proxies) is **agent-framework-agnostic**: it only requires (i) a sandbox runtime that can RPC back to the host, and (ii) a host registry that can answer `search`/`get_schema`/`call`.
- MAF (`Microsoft.Agents.AI`, Python `agent-framework`) ships an MCP **client** + an MCP-aware workflow package (`Microsoft.Agents.AI.Workflows.Declarative.Mcp`). That makes MAF a *consumer* of the MCP server we build — not a host requirement.
- Other frameworks (Semantic Kernel, AutoGen, LangChain via `langchain-mcp`, plain Anthropic SDK, OpenAI Assistants) all consume MCP servers the same way. So the same server reaches every framework for free, provided we do not leak MAF types into the MCP surface.

The non-trivial work is mostly **layering discipline**: keeping the MCP core in its own assembly/package with zero MAF references, and shipping MAF integration as a **separate, optional, side-by-side adapter package** that depends on the core (never the reverse).

### 8.2 Target architecture (layered, framework-agnostic)

The stack is split into **four layers**, each shipped as an independent package. Higher layers depend on lower layers; lower layers never reference higher ones.

```
┌───────────────────────────────────────────────────────────────────┐
│  Layer 4 — Consumers (any framework)                              │
│   MAF Agent · SK · AutoGen · LangChain · Claude Desktop · OpenAI  │
│   ── all talk MCP over streamable-HTTP or stdio ──                │
└───────────────────────────┬───────────────────────────────────────┘
                            │ MCP wire protocol only
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│  Layer 3 — Host adapters (optional, swappable, side-by-side)      │
│   • McpServer.Hosting.AspNetCore   (default, no framework deps)   │
│   • McpServer.Hosting.Stdio        (IDE compat)                   │
│   • McpServer.Hosting.Maf          (MAF AIAgent + middleware)     │
│   • McpServer.Hosting.SemanticKernel (future)                     │
│   Each only knows how to surface the Core to its host runtime.    │
└───────────────────────────┬───────────────────────────────────────┘
                            │ depends on Core abstractions only
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│  Layer 2 — MCP Core (THE PRODUCT — framework-agnostic)            │
│   Tools exposed to clients:                                       │
│    • status                                                       │
│    • search_tools / call_tool          (tool-search pattern)      │
│    • search / get_schema / execute     (staged code-mode, legacy) │
│    • run_code                          (single-call code-mode)    │
│                                                                   │
│   Internal abstractions (pure interfaces, zero framework refs):   │
│    • IToolBackend         (OpenAPI / MCP-server / in-proc)        │
│    • IToolRegistry        (unified, namespaced)                   │
│    • IToolSearcher        (BM25-ish weighted ranker)              │
│    • IUserContextProvider (auth/identity port)                    │
│    • ISandboxRunner       (pluggable execution backend)           │
│    • IRuntimeRpcRouter    (allowlisted in-sandbox callbacks)      │
│    • IOutputFormatter     (compact / JSON / TOON)                 │
│    • IMemoryStore         (optional cross-call scratchpad)        │
└───────────────────────────┬───────────────────────────────────────┘
                            │
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│  Layer 1 — Backends & runners (each in its own package)           │
│   IToolBackend impls:    OpenApi · McpClient · InProc             │
│   ISandboxRunner impls:  OpenSandbox · RootlessContainer · Local  │
│   IMemoryStore impls:    File · Sqlite                            │
│                                                                   │
│   Sandbox runtime (Python entrypoint, identical for all runners): │
│    runtime.search / get_schema / call · list_servers · memory     │
│    mcp_<alias>.<tool>(...) proxies                                │
└───────────────────────────────────────────────────────────────────┘
```

### 8.2a Decoupling principles (non-negotiable)

These are the rules that keep MAF (or any future framework) swappable:

1. **`McpServer.Core` has zero dependencies on MAF, Semantic Kernel, AutoGen, LangChain, OpenAI/Anthropic SDKs, or any LLM client.** Its only allowed external deps are the MCP SDK (`ModelContextProtocol*`), `Microsoft.Extensions.*` abstractions, JSON, and OpenTelemetry **abstractions** (`System.Diagnostics.DiagnosticSource`, never a specific exporter).
2. **Identity and request context cross the boundary as a port (`IUserContextProvider`), not a concrete `MafUserContext`.** Hosts adapt their identity model to the port; the core never imports a host type.
3. **No host-specific tool types in the registry.** A `ToolDescriptor` is plain data + a delegate. MAF's `AIFunction`, SK's `KernelFunction`, etc. are wrapped *at the adapter boundary* into in-process `IToolBackend` entries — never imported into the core.
4. **The MCP wire surface is the only contract.** Adapters expose the Core via MCP transports; consumers (MAF or anyone) talk to it via the standard MCP protocol. There is no "MAF fast path" that bypasses MCP.
5. **Telemetry is emitted through `ActivitySource` only.** Hosts choose exporters (OTLP, MAF DevUI, Application Insights) without the Core caring.
6. **Optional features ship as opt-in packages.** TOON encoding, persistent memory, MAF adapter, container runner — each is a separate package the consumer adds when needed. Defaults stay minimal.
7. **CI gate enforces it.** A test asserts `McpServer.Core.dll` references no `Microsoft.Agents.AI.*`, `Microsoft.SemanticKernel.*`, `Anthropic.*`, `OpenAI.*`, `LangChain.*` assemblies. Any PR that breaks the rule fails the build.

### 8.2b Package layout

```
McpServer.Abstractions      (interfaces + DTOs, no logic)
McpServer.Core              (registry, search, code-mode handlers, RPC router)
McpServer.Backends.OpenApi  (existing OpenApiToolCatalogBuilder lifted out)
McpServer.Backends.Mcp      (wraps an external stdio MCP server as a backend)
McpServer.Sandbox.OpenSandbox
McpServer.Sandbox.Container (rootless podman/docker)
McpServer.Sandbox.Local     (dev-only)
McpServer.Memory.File
McpServer.Output.Toon

McpServer.Hosting.AspNetCore    (default — already exists)
McpServer.Hosting.Stdio         (IDE compat)
McpServer.Hosting.Maf           (OPTIONAL — MAF middleware + AIAgent adapter)
McpServer.Hosting.SemanticKernel (FUTURE example, proves portability)
```

Dependency direction is strictly downward: `Hosting.* → Core → Abstractions`; `Backends.* → Abstractions`; `Sandbox.* → Abstractions`. **No package may reference a peer or anything above it.**

### 8.3 Hypotheses to validate

| ID | Hypothesis | How we test |
|---|---|---|
| H1 | A unified `IToolRegistry` can transparently mix OpenAPI and MCP-server backends behind one descriptor type. | Add `IToolBackend` to `Abstractions`; implement `OpenApiToolBackend` and `McpClientToolBackend`; verify `search`/`get_schema`/`execute` are agnostic. |
| H2 | A single `run_code` tool can replace the staged `search`/`get_schema`/`execute` for code-capable clients without losing discoverability. | Expose both surfaces; A/B compare token usage and success rate on the same prompts. |
| H3 | In-sandbox `runtime.*` helpers can RPC back to the host registry safely (no recursion, no privilege escalation). | Add an allowlisted RPC channel + audit log; replace the regex-based `SandboxCodeGuard` with allowlist + rate-limit + recursion-depth guard. |
| H4 | A sandbox can be reused across calls to retain Python state, gated by session identity, without leaking state across users. | Introduce session-scoped sandbox cache keyed by `IUserContextProvider`; benchmark cold-start savings vs. isolation cost. |
| H5 | The MCP Core is genuinely framework-agnostic. | CI assertion: `McpServer.Core.dll` references *no* MAF/SK/AutoGen/LangChain/OpenAI/Anthropic assemblies. Build a throwaway `Hosting.SemanticKernel` adapter to prove portability. |
| H6 | MAF can be added as a *consumer* of the MCP server with **no changes** to the Core. | Build `McpServer.Hosting.Maf` as a thin adapter that (a) hosts the Core via MAF middleware and (b) exposes it as an `AIAgent`. The Core compiles and tests green without the MAF package being installed. |
| H7 | TOON / compact output reduces downstream prompt cost meaningfully on multi-tool workflows. | Add `IOutputFormatter`; ship `McpServer.Output.Toon` as a separate package; measure tokens per execute on existing brewery prompts. |

### 8.4 Workstreams & milestones

**Phase 0 — Prep (small).**
- Define the package split from §8.2b; create empty projects + reference graph; add the CI assertion that bans framework-specific assemblies from the Core.
- Decide sandbox language: recommend **Python** to reuse upstream's mature entrypoint + `requests` shim. Host language stays **C#/.NET**.
- Read MAF MCP samples ([dotnet samples 02-agents](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/02-agents), [Python 04-hosting](https://github.com/microsoft/agent-framework/tree/main/python/samples/04-hosting)) **only to inform the adapter**, not the Core.

**Phase 1 — Carve out `McpServer.Abstractions` + `McpServer.Core` (H5).**
- Move existing interfaces (`IToolRegistry`, `IToolSearcher`, `ISandboxRunner`) and DTOs into `McpServer.Abstractions`.
- Move handlers (`CodeModeHandlers`, `ToolSearchHandlers`, `MetaTools`, `DiscoveryTools`, `ExecuteTool`) into `McpServer.Core`.
- Add `IUserContextProvider` to replace the singleton `UserContext` so identity is portable.
- Wire the CI ban-list assertion. Existing tests must still pass.

**Phase 2 — Unified backend abstraction (H1).**
- Introduce `IToolBackend { ListAsync, InvokeAsync, GetSchemaAsync }` in `Abstractions`.
- Lift `OpenApiToolCatalogBuilder` into `McpServer.Backends.OpenApi` as `OpenApiToolBackend`.
- Add `McpServer.Backends.Mcp` (`McpClientToolBackend`) using the **vanilla MCP SDK client** — *not* MAF's MCP client — so backends stay framework-free.
- Auto-discovery of backend MCP servers cribbed from upstream's 12+ config locations, behind an explicit opt-in allowlist for safety.

**Phase 3 — In-sandbox runtime API (H3).**
- Define a JSON-framed RPC protocol between sandbox and host (mirror upstream's `handle_rpc` shape) in `McpServer.Abstractions`.
- Add `IRuntimeRpcRouter` with a hard allowlist (`runtime.search`, `runtime.get_schema`, `runtime.call`, `runtime.list_servers`, `runtime.describe`, `runtime.memory.*`).
- Replace `SandboxCodeGuard` regex with structural allowlisting (the router simply has no route for meta-tools) plus a recursion-depth guard.
- Generate `mcp_<alias>` Python proxies inside the sandbox at startup from the registry snapshot.
- Inject `BASE_URL` / OpenAPI base allowlist for direct HTTP fallbacks.

**Phase 4 — Single `run_code` tool (H2).**
- Add `run_code(code, sources?, timeout?)` as the primary code-mode entrypoint, alongside the existing staged tools.
- Move `get_execute_syntax` content into the in-sandbox `runtime.capability_summary()`.
- Keep `search`/`get_schema`/`execute` as a deprecated-but-supported path for non-code-capable clients.

**Phase 5 — Sandbox lifecycle & state (H4).**
- Add session-scoped warm sandbox pool keyed by `IUserContextProvider`.
- Implement upstream-style idle shutdown timer.
- Add `IMemoryStore` + `McpServer.Memory.File` implementation; surface as `runtime.save_memory` / `load_memory`.
- Strict per-session isolation: sandbox processes never share filesystem mounts across sessions.

**Phase 6 — Output formatting (H7).**
- Add `IOutputFormatter` to `Abstractions`.
- Ship `McpServer.Output.Toon` as a separate opt-in package (port upstream's encoder; fall back to JSON if unavailable).
- Default formatter drops empty fields from `ExecuteResponse`.

**Phase 7 — Host adapters (H6, the MAF integration).**
- Keep the existing ASP.NET Core host as `McpServer.Hosting.AspNetCore`.
- Add `McpServer.Hosting.Stdio` for IDE clients.
- Add `McpServer.Hosting.Maf` as a **separate, optional** package that:
  - Hosts the Core through MAF's request pipeline.
  - Provides an `AIAgentAdapter` so MAF workflows can call the registry as an `AIAgent` (in addition to the MCP wire surface).
  - Adapts MAF's identity context to `IUserContextProvider`.
  - Is the *only* package allowed to reference `Microsoft.Agents.AI.*`.
- Build a throwaway `McpServer.Hosting.SemanticKernel` adapter as a portability proof (**H5**); delete after CI green.

**Phase 8 — Hardening & docs.**
- Threat-model the runtime RPC channel (rate limits, payload size caps, depth limits, audit log).
- Update `SECURITY.md`, `README.md`, `tasks.md`.
- Add a benchmark harness comparing five configurations: (a) tool-search only, (b) staged code-mode, (c) `run_code` ephemeral, (d) `run_code` warm, (e) `run_code` warm via MAF host (to prove the adapter does not regress).

### 8.5 Acceptance criteria

- **Decoupling proof**: `McpServer.Core` (and every `Backends.*`/`Sandbox.*` package) compiles, tests, and runs end-to-end **without `Microsoft.Agents.AI.*` installed**. CI fails any PR that adds a banned reference.
- **Portability proof**: a scratch `McpServer.Hosting.SemanticKernel` (or any non-MAF) adapter compiles against the Core unchanged.
- **Composition**: a code-mode prompt that requires composing **3 different tools across an OpenAPI source and a backend MCP server** completes in **one** `run_code` round-trip with verifiable telemetry.
- **Surface**: the MCP server's `tools/list` returns ≤ 4 visible tools by default; per-tool schema bytes are emitted only on demand.
- **Token budget**: a representative benchmark shows ≥ 50% reduction in prompt tokens vs. the current staged pipeline on multi-tool tasks.
- **Adapter parity**: the same Core instance is consumable as (a) raw MCP over HTTP, (b) MCP over stdio, and (c) an MAF `AIAgent` — with identical tool semantics and no duplicated tool definitions.
- **Existing tests** in `tests/McpServer.UnitTests` and `tests/McpServer.ContractTests` remain green; new tests cover the CI ban-list, RPC allowlist, warm-sandbox session isolation, and TOON output.

### 8.6 Risks & mitigations

| Risk | Likelihood | Mitigation |
|---|---|---|
| **MAF leaks into the Core** under deadline pressure (the most important risk). | High | CI ban-list assertion is a build gate; PR template question "does this add a framework dependency to Core?"; quarterly portability check via the SK scratch adapter. |
| MAF MCP client / `AIAgent` API churn. | High | Adapter is the *only* MAF reference; pin versions per release; the rest of the stack is unaffected. |
| Warm sandbox sessions leak state across users. | Medium | Strict per-session keying via `IUserContextProvider`; aggressive idle timeout; no shared FS mounts across sessions. |
| In-sandbox RPC enables prompt-injection-driven privilege escalation. | Medium | Hard allowlist of RPC verbs; per-call payload caps; audit log; recursion-depth guard; deny `run_code` from inside `run_code`. |
| Mixing OpenAPI + MCP backends produces name collisions. | Low | Namespace by backend (`openapi.brewery_search` vs. `mcp.serena.search`); unique aliases enforced at registration. |
| OpenSandbox provider availability/cost (external dependency). | Medium | Keep rootless-podman/docker runner as a peer option in `McpServer.Sandbox.Container`. |
| Token-bloat improvements don't materialize because IDE clients still cache schemas. | Low | Measure before/after with real LLM API token counters, not just byte counts. |
| Adapter packages duplicate Core logic by accident. | Medium | Adapters are kept minimal-by-policy; review checklist forbids tool registration or search logic in any `Hosting.*` package. |

### 8.7 Open questions

1. Should the sandbox runtime be **language-pluggable** (Python today, C# Roslyn later) or **Python-only** to inherit upstream's mature entrypoint?
2. Where does the **persistent memory** live by default — `McpServer.Memory.File` or `McpServer.Memory.Sqlite`? (MAF's `Microsoft.Agents.AI.FoundryMemory` would be a *third*, optional adapter package — never the default, never in the Core.)
3. Does the MCP Core expose **arbitrary host-registered functions as tools** via an in-process `IToolBackend` (so MAF agents, SK functions, or plain C# delegates can be surfaced uniformly)? This is the cleanest way to let any framework contribute tools without coupling.
4. How do we model **workflow checkpointing** (an MAF feature) — keep it MAF-only inside the adapter, or define a Core-level `ISessionCheckpoint` port that MAF, Durable Functions, and others can implement?
5. Do we keep the `search_tools` / `call_tool` legacy surface long-term, or sunset it once `run_code` lands? (Argument for keeping: clients without code-execution still need it.)
6. Should the `Hosting.Maf` adapter ship in this repo or live in a sibling repo to make the decoupling visually obvious?

### 8.8 Recommended sequencing

> Smallest viable path to demonstrate the thesis:
>
> **P1 (Core/Abstractions split + CI ban-list) → P2 (`IToolBackend`) → P3 (in-sandbox RPC, replacing the regex guard) → P4 (`run_code`)** is the **critical path**. None of these touch MAF.
>
> **P7 (host adapters, including MAF)** runs *strictly after* P1 and proves the decoupling. The MAF adapter can be deferred indefinitely without blocking the rest of the work.
>
> P5 (warm sessions) and P6 (TOON) are **incremental wins**, ship as opt-in packages, feature-flag in the host.

This plan delivers a **framework-agnostic MCP server** that (i) keeps this repo's OpenAPI strengths, (ii) adopts upstream's one-round-trip in-sandbox composition algorithm, and (iii) treats MAF as one of several interchangeable host adapters — so swapping to Semantic Kernel, AutoGen, LangChain, or plain ASP.NET Core later is a packaging change, not a rewrite.
