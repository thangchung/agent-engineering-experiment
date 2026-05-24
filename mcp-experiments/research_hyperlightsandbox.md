# Hyperlight Sandbox (.NET SDK) — research and integration plan

> Goal: understand the [`hyperlight-sandbox` .NET SDK](https://github.com/hyperlight-dev/hyperlight-sandbox/tree/main/src/sdk/dotnet) deeply, then design (not implement) a new code-mode sandbox provider for this repo — `hyperlight-sandbox` — that sits next to the existing OpenSandbox provider behind [`ISandboxRunner`](src/McpServer/CodeMode/ISandboxRunner.cs).

---

## 1. What is `hyperlight-sandbox`?

[Hyperlight](https://github.com/hyperlight-dev/hyperlight) is a Microsoft-incubated, OSS micro-VM execution engine. `hyperlight-sandbox` is a higher-level project on top of it that exposes a "secure code execution" sandbox with three SDKs (Rust, Python, .NET). It is purpose-built to host AI-agent code execution: the README explicitly calls out `CodeExecutionTool` + `AIFunction` integration with the **GitHub Copilot SDK** and **Microsoft Agent Framework**.

Two distinguishing properties versus typical container sandboxes:

1. **Workload runs in a Hyperlight micro-VM**, not a Linux container. Each sandbox is a CPU/memory-isolated VM with no kernel, no syscalls into the host, and no networking by default. The guest is a WebAssembly component (Python or JavaScript bundled) or a built-in QuickJS runtime.
2. **Snapshot / restore** is a first-class primitive. After a cold start (~2.5 s for the Python guest), you snapshot the clean post-init state and restore (~2 ms, ~1000× faster) between executions. This is the "warm pool" pattern, but built into the runtime.

---

## 2. The .NET SDK in depth

### 2.1 Layered architecture

From [README.md](https://github.com/hyperlight-dev/hyperlight-sandbox/blob/main/src/sdk/dotnet/README.md):

```
.NET app
  │
  ├── HyperlightSandbox.Api           ← Sandbox, SandboxBuilder, ExecutionResult
  ├── HyperlightSandbox.Extensions.AI ← CodeExecutionTool → AIFunction
  └── HyperlightSandbox.PInvoke       ← SafeNativeMethods, SafeHandles, FFIResult
                │  P/Invoke
                ▼
       hyperlight_sandbox_ffi (cdylib, Rust)
                │
                ▼
       hyperlight-sandbox (Rust core) + Wasm + JS-via-QuickJS guests
```

### 2.2 NuGet packages (5)

| Package | Role |
|---|---|
| `Hyperlight.HyperlightSandbox.PInvoke` | P/Invoke bindings + native cdylib (`libhyperlight_sandbox_ffi.so`) |
| `Hyperlight.HyperlightSandbox.Api` | High-level `Sandbox`, `SandboxBuilder`, `ExecutionResult`, `SandboxSnapshot` |
| `Hyperlight.HyperlightSandbox.Extensions.AI` | `CodeExecutionTool`, `.AsAIFunction()` for Copilot SDK / MAF |
| `Hyperlight.HyperlightSandbox.Guest.Python` | Bundled Python `.wasm`/`.aot` guest + `WithPythonModule()` extension |
| `Hyperlight.HyperlightSandbox.Guest.JavaScript` | Bundled JS `.wasm`/`.aot` guest + `WithJavaScriptModule()` extension |

### 2.3 Public surface (managed)

`SandboxBuilder` (fluent):

- `WithModulePath(string)` — required for the Wasm backend.
- `WithPythonModule()` / `WithJavaScriptModule()` — convenience from the bundled guest packages.
- `WithBackend(SandboxBackend.Wasm | .JavaScript)` — `JavaScript` uses the built-in QuickJS engine (no module path).
- `WithHeapSize(string|ulong)`, `WithStackSize(string|ulong)` — accept `"50Mi"`/`"35Mi"` etc., parsed by `SizeParser`.
- `WithInputDir(path)` — read-only `/input` inside the guest.
- `WithOutputDir(path)` / `WithTempOutput()` — writable `/output` (host or temp dir).
- `Build()` — returns `Sandbox`.

`Sandbox` (sealed, IDisposable):

- `Run(code) → ExecutionResult { Stdout, Stderr, ExitCode, Success }`; sync.
- `RunAsync(code, ct)` — `Task.Run(() => Run(code), ct)`; cancellation prevents scheduling but **cannot interrupt an in-flight FFI call**.
- `RegisterTool<TArgs,TResult>(name, handler)` — typed tool dispatched via `call_tool("name", ...)` from inside the guest. Uses `System.Text.Json` to (de)serialize.
- `RegisterTool(name, Func<string,string>)` — raw-JSON variant.
- `RegisterToolAsync<...>(...)` — async handler is blocked on at the FFI boundary via `GetAwaiter().GetResult()` (FFI callbacks have no `SynchronizationContext`, so this is safe-ish).
- `AllowDomain(target, methods?)` — outbound HTTP allowlist with optional method filter (`["GET","POST"]`).
- `GetOutputFiles()` / `OutputPath` — list/inspect what the guest wrote to `/output`.
- `Snapshot()` → `SandboxSnapshot` (also `IDisposable`).
- `Restore(snapshot)` — non-consuming; the same snapshot can be restored repeatedly.

`ExecutionResult` is a record `(Stdout, Stderr, ExitCode)` with `Success => ExitCode == 0`.

### 2.4 Exceptions (typed)

The Rust FFI returns a `FFIResult { is_success, error_code, value }` and an `FFIErrorCode` enum that the .NET layer maps to typed exceptions, **without string matching** (per [PR #292 lesson recorded in the FFI source](https://raw.githubusercontent.com/hyperlight-dev/hyperlight-sandbox/main/src/sdk/dotnet/ffi/src/lib.rs)):

| Code | C# exception |
|---|---|
| `Success = 0` | — |
| `Unknown = 1` | `SandboxException` |
| `Timeout = 2` | `SandboxTimeoutException` |
| `Poisoned = 3` | `SandboxPoisonedException` (sandbox unrecoverable, recreate) |
| `PermissionDenied = 4` | `SandboxPermissionException` (network allowlist) |
| `GuestError = 5` | `SandboxGuestException` (guest code raised) |
| `InvalidArgument = 6` | `ArgumentException`-style |
| `IoError = 7` | `IOException`-style |

### 2.5 Threading & lifecycle invariants

- **Send-but-not-Sync**: every public method on `Sandbox` takes an internal `lock (_gate)`. Concurrent calls serialize, they do not deadlock. For throughput, use one sandbox per worker thread.
- **Lazy init**: configuration (`set_input_dir`, `set_output_dir`, `set_temp_output`, `register_tool`, `allow_domain`) is collected before the first `Run`. The first `Run` builds the actual `SandboxBuilder` inside Rust (`ensure_initialized`), registers the `ToolRegistry`, applies queued domains, and constructs `BackendSandbox::Wasm | ::Js`. **`register_tool` after init throws `InvalidArgument`.**
- **Tool callbacks are pinned**: each registered delegate is wrapped in a `ToolCallbackDelegate`, pinned via `GCHandle.Alloc`, and the function pointer (`Marshal.GetFunctionPointerForDelegate`) is handed to Rust. Forgetting to pin would cause a SIGSEGV on the first guest `call_tool` — the source comments call this out explicitly.
- **Disposal order is critical**: `Sandbox.Dispose()` (1) releases the native handle via the `SafeHandle`, then (2) frees the pinned `GCHandle`s. The destructor does the same so callbacks remain valid even during native cleanup if the user forgot to dispose.
- **`CodeExecutionTool.Dispose` deliberately does not call `_sandbox.Dispose()` from the finalizer** — that would acquire a lock from a finalizer thread and risk deadlocking. The SafeHandle finalizer of `Sandbox` handles it instead.

### 2.6 Tool dispatch wire format

- Args travel as a UTF-8 JSON string. The tool callback signature is `unsafe extern "C" fn(args_json: *const c_char) -> *mut c_char`.
- The .NET callback returns a JSON string allocated with `Marshal.StringToCoTaskMemUTF8`. Rust **copies** the string, then frees it with `libc::free` (Linux) or `CoTaskMemFree` (Windows) — matching the allocator that .NET used.
- Error convention: a tool returns `{"error": "message"}` to signal a failure. Rust unpacks this and surfaces it as `anyhow::bail!("tool '<n>': <msg>")`, which the host-side `Run` then surfaces to the guest as a `call_tool` exception.
- Optional schema string: `{"args": {"a":"Number","b":"String"}, "required": ["a"]}` — Rust parses this into `ToolSchema` for argument validation. Types: `Number | String | Boolean | Object | Array`.

### 2.7 `CodeExecutionTool` (`Extensions.AI`)

This is the agent-facing wrapper. It is what makes Hyperlight feel like a "code interpreter tool" for an LLM:

```csharp
using var tool = new CodeExecutionTool(new SandboxBuilder()
    .WithPythonModule()
    .WithTempOutput());

tool.RegisterTool<AddArgs, AddResult>("add", a => new AddResult { Sum = a.A + a.B });
var aiFunc = tool.AsAIFunction();        // Microsoft.Extensions.AI.AIFunction
```

Key behavior:

- **First `Execute(code)`** calls `_sandbox.Run("None")` (Wasm) or `_sandbox.Run("void 0;")` (JS) to force lazy init, then takes a `Snapshot()` of the clean post-init state.
- **Every subsequent `Execute(code)`** does `_sandbox.Restore(_cleanSnapshot)` then `_sandbox.Run(code)`. This is the 1000× faster reset — no spin-up cost, no state leaking between calls.
- `AsAIFunction(name = "execute_code", description = …)` returns a `Microsoft.Extensions.AI.AIFunction` that takes a single `code` string parameter and returns `{stdout, stderr, exit_code, success}` JSON. This plugs straight into MAF (`ChatOptions.Tools = [aiFunc]`) or Copilot SDK (`SessionConfig.Tools = [aiFunc]`).

### 2.8 FFI surface (Rust → C)

Documented in [`ffi/src/lib.rs`](https://github.com/hyperlight-dev/hyperlight-sandbox/blob/main/src/sdk/dotnet/ffi/src/lib.rs). All entry points are `unsafe extern "C"` and return `FFIResult`:

| FFI export | Purpose |
|---|---|
| `hyperlight_sandbox_get_version` | Returns crate semver. |
| `hyperlight_sandbox_create(FFISandboxOptions)` | Builds an opaque `*mut SandboxState` handle (lazy — no Wasm load yet). |
| `hyperlight_sandbox_free(handle)` | Drops the handle. |
| `hyperlight_sandbox_set_input_dir/output_dir/temp_output` | Pre-init filesystem config. |
| `hyperlight_sandbox_register_tool(handle, name, schema_json?, ToolCallbackFn)` | Pre-init only. |
| `hyperlight_sandbox_allow_domain(handle, target, methods_json?)` | Queue or apply network rule. |
| `hyperlight_sandbox_run(handle, code) → JSON {stdout,stderr,exit_code}` | Lazy-init on first call, then execute. |
| `hyperlight_sandbox_get_output_files` / `_output_path` | Inspect `/output` after init. |
| `hyperlight_sandbox_snapshot/_restore/_free_snapshot` | Snapshot lifecycle. |
| `hyperlight_sandbox_free_string` | Free strings returned across the FFI. |

`FFISandboxOptions` is a `[repr(C)]` struct with `module_path`, `heap_size`, `stack_size`, `backend (0=Wasm, 1=JS)`. Zero sizes mean "platform default".

Internal `SandboxState`:

```text
inner:            Option<BackendSandbox>     // None until first run
backend:          FFIBackend
tools:            HashMap<String, ToolEntry> // collected before init
pending_networks: Vec<(String, Option<Vec<String>>)>
config:           SandboxConfig (module_path, heap, stack)
input_dir / output_dir / temp_output
```

`build_tool_registry` wraps each .NET callback in a Rust closure that: serializes args → `CString`, calls the function pointer, copies the response, frees the CoTaskMem allocation, and reparses to `serde_json::Value`. The `{"error": …}` convention is detected at this layer.

### 2.9 Quirks worth knowing

- **Linux only at present** (Windows tracked upstream). The cdylib is built with `just dotnet dist`, which the README documents as a packaging step that produces `dist/dotnetsdk/`.
- **No managed sandbox-creation timeout**: there is no `TimeoutSeconds` field analogous to OpenSandbox. Per-call timeouts must be enforced externally (e.g., a watchdog `Task.Delay(timeout)` racing the `RunAsync`). An in-flight FFI call cannot be cancelled — the watchdog can only abandon the result and dispose the sandbox.
- **No remote dependency**: Hyperlight runs the VM in-process via KVM/Hyper-V. There is no equivalent of OpenSandbox's `SANDBOX_DOMAIN` / `SANDBOX_API_KEY` and no network round-trip per call.
- **No native HTTP client**: HTTP is mediated by the **host** through `AllowDomain`. The Wasm guests provide a `http_get` / `http_post` shim that calls into host code; arbitrary `import requests` works only because the bundled Python guest reroutes it through this shim. There is no need for the `requests`-via-`urllib` shim that this repo's `OpenSandboxRunner` injects for `python:3.12-slim`.
- **`Tools` and `AllowDomain` are different lifecycles**: tools must be registered before first `Run`; domains can be added before *or* after init.

---

## 3. Comparison with this repo's existing `OpenSandboxRunner`

| Dimension | Hyperlight | OpenSandbox (this repo) |
|---|---|---|
| Isolation primitive | Hyperlight micro-VM (KVM/Hyper-V) | Container managed by the OpenSandbox API |
| Process model | In-process via P/Invoke + cdylib | Out-of-process, remote API (HTTP) |
| Cold start | ~2.5 s (Python guest) | Several seconds (image pull + container spin-up) |
| Warm reset | `Snapshot/Restore` (~2 ms) | None — per-call ephemeral container |
| Guest language | Python or JavaScript (bundled Wasm guests) or QuickJS | Whatever image you pick (`python:3.12-slim` here) |
| Tool dispatch into host | First-class `RegisterTool` + `call_tool()` from guest | None (guest cannot call host tools — code is fully isolated; this repo enforces it via [`SandboxCodeGuard`](src/McpServer/CodeMode/SandboxCodeGuard.cs)) |
| Network policy | `AllowDomain(target, methods?)` enforced by host | Container has full egress; allowlist enforced by [`LocalConstrainedRunner`](src/McpServer/CodeMode/Local/LocalConstrainedRunner.cs) regex (not by Hyperlight at the kernel level) |
| Filesystem | `/input` (RO) + `/output` (RW) | Implicit container FS; not exposed |
| Cancellation | None mid-FFI; external watchdog | Cooperative via `CancellationToken` propagated to OpenSandbox SDK |
| Platform | Linux (Windows WIP) | Cross-platform (calls remote service) |
| External dependency | None (in-proc) | OpenSandbox server (managed by this repo's `opensandbox-server` Aspire resource) |
| Best fit | Single-host, low-latency, repeated calls; agent-style "code interpreter" | Multi-host or hosted scenarios; ephemeral isolation per call |

### 3.1 Architectural differences vs. this repo's runner contract

Today the contract is intentionally minimal:

```csharp
// src/McpServer/CodeMode/ISandboxRunner.cs
public interface ISandboxRunner
{
    string SyntaxGuide { get; }
    Task<RunnerResult> RunAsync(string code, CancellationToken ct);
}
```

`RunnerResult` is `(object? FinalValue, int CallsExecuted)`. The current Python convention is *"set a `result` variable; we'll JSON-serialize and return it"*, with stdout fallback. Both [`OpenSandboxRunner`](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs) and [`LocalConstrainedRunner`](src/McpServer/CodeMode/Local/LocalConstrainedRunner.cs) implement that contract by wrapping the user code in a Python harness that prints a single JSON line `{"ok":..,"finalValue":..,"stdout":..,"stderr":..}`.

Mapping that onto Hyperlight is straightforward: `ExecutionResult.Stdout` already contains everything the harness prints, and the same harness can be passed verbatim into `Sandbox.Run(code)`. No host-side parsing changes are needed.

---

## 4. Design: a `hyperlight-sandbox` provider for this repo

### 4.1 Goal

Add a third sandbox runner — `HyperlightSandboxRunner` — selectable via `CodeMode:Runner=hyperlight`, sitting next to the existing `local` and `opensandbox` options. It implements the same [`ISandboxRunner`](src/McpServer/CodeMode/ISandboxRunner.cs) contract, the existing `RunnerResult` shape, and the same Python `result =` convention used by [`OpenSandboxRunner`](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs). No callers (`ExecuteTool`, `CodeModeHandlers`, MCP surface) need to change.

### 4.2 Folder & file layout (mirrors `CodeMode/OpenSandbox/`)

```
src/McpServer/CodeMode/HyperlightSandbox/
    HyperlightSandboxRunner.cs            ← ISandboxRunner impl
    HyperlightSandboxRunnerOptions.cs     ← config DTO + enums
    IHyperlightSession.cs                 ← thin port over Sandbox + Snapshot
    HyperlightSessionAdapter.cs           ← real adapter wrapping HyperlightSandbox.Api.Sandbox
    StubHyperlightSession.cs              ← used when packages not installed (cross-platform dev)
    HyperlightCodeHarness.cs              ← reuses the existing Python harness
```

Why a `IHyperlightSession` port instead of binding directly to `HyperlightSandbox.Api.Sandbox`?

1. **Platform**: Hyperlight is Linux-only today. Anyone on macOS/Windows must still be able to `dotnet build` the repo and run unit tests. Hiding the SDK behind an interface lets us ship a stub adapter that throws `PlatformNotSupportedException` only when a real session is opened.
2. **Testability**: it mirrors the pattern already used in the codebase — see the `remoteExecutor` ctor parameter on [`OpenSandboxRunner`](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs#L62). Tests can swap a fake session and assert on the dispatched code without touching the FFI.

### 4.3 Configuration (extension of `appsettings.json`)

```jsonc
{
  "CodeMode": {
    "Runner": "hyperlight",
    "TimeoutMs": 5000,
    "MaxToolCalls": 10
  },
  "Hyperlight": {
    "Guest": "Python",        // Python | JavaScript | QuickJs | Custom
    "ModulePath": null,        // required when Guest = Custom
    "HeapSize": "50Mi",
    "StackSize": "35Mi",
    "InputDir": null,
    "TempOutput": true,
    "UseWarmSnapshot": true,   // mirrors CodeExecutionTool's snapshot/restore loop
    "AllowedDomains": [
      { "Target": "https://api.openbrewerydb.org", "Methods": ["GET"] },
      { "Target": "https://petstore3.swagger.io",  "Methods": ["GET"] }
    ]
  }
}
```

### 4.4 Runner behavior (mirrors `OpenSandboxRunner` contract)

```text
RunAsync(code, ct):
  1.  SandboxCodeGuard.ContainsForbiddenMetaToolUsage(code) → throw  (parity)
  2.  Wrap user code in Python harness identical to OpenSandboxRunner.BuildPythonCommand
      (the harness prints one JSON line at the end → {"ok":..,"finalValue":..})
  3.  Acquire a session from the configured factory.
        - If UseWarmSnapshot: lazily build, run init no-op, snapshot once, restore-then-run for
          every subsequent call (the CodeExecutionTool pattern).
        - Else: build a fresh session per call.
  4.  Apply CodeMode:TimeoutMs as an external watchdog (Task.Delay race + dispose-on-timeout)
      because the SDK has no in-FFI cancellation.
  5.  result = await session.RunAsync(harness, ct).
      Map result.ExitCode != 0 to the same exception class used today.
  6.  Parse the last stdout line as the same SandboxExecutionPayload used by OpenSandbox
      (reuse src/McpServer/CodeMode/OpenSandbox/SandboxExecutionPayload.cs).
  7.  Return RunnerResult(finalValue, 0)  — CallsExecuted stays 0 per the OpenSandbox precedent.
```

Telemetry: emit on a new `ActivitySource("McpServer.CodeMode.HyperlightSandboxRunner")` with tags `mcp.sandbox.guest`, `mcp.sandbox.warm`, `mcp.Execute.timeout`, `mcp.Execute.hasFinalValue` — keeping the schema parallel to OpenSandbox.

### 4.5 Factory wiring ([`SandboxRunnerFactory.cs`](src/McpServer/CodeMode/SandboxRunnerFactory.cs))

Add a third branch above the local fallback:

```text
runnerName == "hyperlight"   → new HyperlightSandboxRunner(options, loggerFactory, sessionFactory)
runnerName == "opensandbox"  → existing branch (unchanged)
default                      → LocalConstrainedRunner (unchanged)
```

The factory binds `Hyperlight:*` config into `HyperlightSandboxRunnerOptions`. The session factory picks `HyperlightSessionAdapter` on Linux (via `RuntimeInformation.IsOSPlatform`) and `StubHyperlightSession` otherwise.

### 4.6 NuGet dependencies (added to [`McpServer.csproj`](src/McpServer/McpServer.csproj))

```xml
<PackageReference Include="Hyperlight.HyperlightSandbox.Api"               Version="0.4.*" />
<PackageReference Include="Hyperlight.HyperlightSandbox.Extensions.AI"    Version="0.4.*" />
<PackageReference Include="Hyperlight.HyperlightSandbox.Guest.Python"     Version="0.4.*" />
```

Notes:

- Only the `Guest.*` package matching `Hyperlight:Guest` needs to be referenced. Default to Python because the existing harness is Python.
- `Extensions.AI` is optional but worth pulling in: it lets us also expose `HyperlightSandboxRunner` as an `Microsoft.Extensions.AI.AIFunction` later without writing a second adapter (relevant to the §8 plan in `research_mcp.md`).
- Native `libhyperlight_sandbox_ffi.so` is only available for Linux today. Expect package restore to succeed on macOS but `Sandbox` construction to fail at runtime — hence the stub.

### 4.7 Aspire & deployment impact

Unlike OpenSandbox, Hyperlight has **no remote service**. There is no equivalent of `opensandbox-server` to add to `aspire.config.json` or `docker-compose.yaml`. The `mcp-server` container itself becomes the sandbox host, which means:

- The container must run on a Hyperlight-capable host (Linux + KVM, or Windows + Hyper-V when GA). For the Aspire local profile this means Linux/WSL2.
- The container image must include the native cdylib that ships with the PInvoke NuGet (verify it's contained in the published `runtimes/linux-x64/native/` folder of the package, otherwise add a build step).
- On Kubernetes (`deploy/aks/`), the pod likely needs `securityContext.privileged: true` or a runtime class that allows `/dev/kvm` access. This is the biggest operational difference vs. OpenSandbox and must be documented before recommending Hyperlight as the default.

### 4.8 Test strategy (mirrors [`SandboxRunnerFactoryTests`](tests/McpServer.UnitTests/CodeMode/SandboxRunnerFactoryTests.cs))

1. `Create_HyperlightRunnerSelected` — `CodeMode:Runner=hyperlight` returns `HyperlightSandboxRunner`.
2. `Create_HyperlightRunnerWithoutGuestUsesPythonByDefault`.
3. `HyperlightSandboxRunner_RunAsync_DispatchesHarness` — fake `IHyperlightSession`, assert the wrapped harness is what the session receives, parse a synthetic stdout payload, expect the unwrapped `result` value.
4. `HyperlightSandboxRunner_RunAsync_RejectsMetaToolCalls` — parity with [`OpenSandboxRunner`](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs#L92).
5. `HyperlightSandboxRunner_WarmSnapshot_ReusesSession` — fake session with snapshot/restore counters, assert init runs once and restore runs N-1 times for N calls.
6. (Optional, `[Trait("Category","linux-only")]`) `HyperlightSandboxRunner_RealSession_RoundTrip` — guarded integration test exercising the actual Python guest with `result = 1 + 1`.

### 4.9 What this provider unlocks beyond parity

Even though the immediate goal is parity with OpenSandbox, the Hyperlight surface enables features the current MCP server cannot get cheaply elsewhere — and these line up with the "best of breed" research in [research_mcp.md](research_mcp.md):

- **Warm sessions** (snapshot/restore) make the §8 H4 hypothesis (session-scoped sandbox cache) trivial to implement: `_cleanSnapshot` is the cache key, and `Restore + Run + Snapshot` cleanly per-call.
- **Host-side `RegisterTool`** is a first-class implementation of the in-sandbox runtime RPC channel proposed in §8.4 / Phase 3 — guest Python can call `call_tool("runtime.search", …)` instead of going through a regex-based code guard.
- **`AllowDomain`** is a real network policy enforced by the runtime, not a regex. This is a strict upgrade over [`LocalConstrainedRunner.AbsoluteHttpUrlRegex`](src/McpServer/CodeMode/Local/LocalConstrainedRunner.cs).
- **`AsAIFunction()`** integrates directly with MAF and the GitHub Copilot SDK referenced in this repo (`GitHub.Copilot.SDK` is already a dependency in [`McpServer.csproj`](src/McpServer/McpServer.csproj)).

### 4.10 Known risks and decisions to take

| Risk | Mitigation |
|---|---|
| Linux-only native FFI breaks dev builds on macOS/Windows. | `IHyperlightSession` port + stub adapter; tests stay platform-agnostic; document `CodeMode:Runner=hyperlight` as Linux-only. |
| In-flight FFI calls cannot be cancelled. | Watchdog timer disposes the sandbox; surface as `TimeoutException` with the same message as OpenSandbox. |
| Pinned `GCHandle` lifetime mistakes (SIGSEGV). | Wrap the SDK as-is in `HyperlightSessionAdapter`; do not re-implement tool registration. The SDK already pins delegates correctly. |
| Snapshot drift: a mistakenly-stateful warm session leaks values across calls. | Always restore *before* `Run`; never run user code without an immediately preceding restore. Mirror `CodeExecutionTool.Execute` exactly. |
| Different guest produces different syntax expectations. | `SyntaxGuide` is dynamic per `Hyperlight:Guest`; mirror the existing "Runner: OpenSandbox (Python)" wording but say "Runner: Hyperlight ({guest})". |
| K8s requires `/dev/kvm`. | Document privileged pod requirement explicitly in [deploy/aks/deployment.yaml](deploy/aks/deployment.yaml) before promoting Hyperlight to default. |
| SDK churn at 0.4.x. | Pin patch version; isolate behind `IHyperlightSession`. |

### 4.11 Open questions

1. Should we keep the Python harness, or switch to the Hyperlight `RegisterTool`+`call_tool` model so the sandbox calls back into the host registry? The latter aligns directly with the §8 plan in [research_mcp.md](research_mcp.md) but is a larger change.
2. Do we need `WithInputDir`/`WithOutputDir` plumbed through MCP, or are runs always purely computational? (Today none of the OpenAPI-backed tools produce files.)
3. Should `Hyperlight:UseWarmSnapshot` default to `true` (matches `CodeExecutionTool`) or `false` (matches the per-call ephemeral OpenSandbox model)?  Lean `true` — it's the whole point of choosing Hyperlight.

### 4.12 Phased delivery (no code in this report)

- **P1** — Add options + interface + stub + factory branch; runtime returns `PlatformNotSupportedException` on non-Linux. Tests for factory wiring.
- **P2** — Implement `HyperlightSessionAdapter` against `HyperlightSandbox.Api.Sandbox`. Implement runner with the existing Python harness, no warm snapshot.
- **P3** — Add warm-snapshot loop (init → snapshot once → restore + run per call). Add session disposal on timeout.
- **P4** — Add `AllowDomain` driven by `OpenAPI:Sources` base URLs (replace the regex-based guard for this provider). Add Linux-only integration tests.
- **P5** — (Optional) Switch host↔guest from "harness with `result =`" to `RegisterTool`-based RPC, aligning with §8.

---

## 5. Summary

Hyperlight Sandbox is, in shape, the .NET-native answer to "I want a Python/JS code-execution sandbox for an LLM agent, in-process, with sub-millisecond warm starts and host-callable tools". Its .NET SDK is a clean three-layer stack (Api → PInvoke → Rust cdylib) with disciplined GC pinning, typed FFI errors, opaque `SafeHandle`-based ownership, and an `Extensions.AI` package that exposes the whole thing as a single `AIFunction` for MAF / Copilot SDK.

Adopting it as a third [`ISandboxRunner`](src/McpServer/CodeMode/ISandboxRunner.cs) provider in this repo is **straightforward at the contract level** — drop-in next to [`OpenSandboxRunner`](src/McpServer/CodeMode/OpenSandbox/OpenSandboxRunner.cs), reuse the existing Python harness and `SandboxExecutionPayload`, hide the FFI behind a small `IHyperlightSession` port for cross-platform builds, and pick up snapshot/restore as a free upgrade. The **non-trivial** parts are operational: Linux-only today, `/dev/kvm` access on Kubernetes, and the absence of in-FFI cancellation — none of which block a working prototype, but all of which have to be acknowledged before promoting Hyperlight as the default code-mode backend.

---

## 6. MacOS + devcontainer reality check (caveman)

Yes, Linux env needed.

- Build/restore only: Ubuntu devcontainer good.
- Real Hyperlight run: need Linux host + virtualization support (`/dev/kvm` class access).
- Mac Docker/devcontainer usually no KVM passthrough -> runtime test likely fail.

So:

1. Dev on Mac OK.
2. Compile + unit-test in Ubuntu devcontainer OK.
3. Validate real Hyperlight execution on Linux VM/bare-metal runner (or CI Linux runner with KVM).

Refs in this doc:

- Linux-only note: see §2.9.
- Host/runtime requirement (`Linux + KVM`, `/dev/kvm` on K8s): see §4.7.
- Risk + mitigation (`CodeMode:Runner=hyperlight` Linux-only): see §4.10.
