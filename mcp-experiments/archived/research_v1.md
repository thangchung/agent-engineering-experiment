# FastMCP Research v1 (Minimal OpenSandbox Adaptation)

Date: 2026-03-23

## Goal
Define the smallest safe set of changes to adapt current code-mode execution to OpenSandbox, while keeping the current architecture and synthetic tool surface stable.

## What Was Checked Carefully

1. FastMCP behavior model (tool-search and code-mode) from docs/tests.
2. Current repository wiring points:
- `ISandboxRunner` contract already isolates execution backend.
- `ExecuteTool` already delegates runtime to `ISandboxRunner`.
- `Program.cs` already swaps runner via DI (`ISandboxRunner`).
3. OpenSandbox C# SDK usage from NuGet/README style examples:
- `ConnectionConfig` + `Sandbox.CreateAsync(...)`
- command execution via `sandbox.Commands.RunAsync(...)`
- async disposal and optional `sandbox.KillAsync()`
- package is pre-release (`Alibaba.OpenSandbox` 0.1.0-alpha.3)

## Key Conclusion

The repo is already structured for minimal-change runner replacement.

You do not need to redesign tool-search, discovery, execute tool contract, or workflow coordinator to adopt OpenSandbox.

## Minimum-Change Adaptation Plan (OpenSandbox)

### 1. Keep all existing code-mode APIs stable

No change to public synthetic tools:
- keep `search`
- keep `get_schema`
- keep `execute(code)`

No change to registry/search architecture:
- keep `MetaTools`, `DiscoveryTools`, `ExecuteTool`, `WorkflowCoordinator`

### 2. Add one new runner implementation only

Create a new runner:
- `OpenSandboxRunner : ISandboxRunner`

Behavior contract remains identical:
- input: `RunAsync(string code, ToolBridge bridge, CancellationToken ct)`
- output: `RunnerResult` (final value + call count)

Important: keep the same constrained language semantics currently enforced by `LocalConstrainedRunner` so model behavior does not regress.

### 3. Minimal DI switch in Program

Current line registers local runner:
- `AddSingleton<ISandboxRunner>(... LocalConstrainedRunner ...)`

Minimal adaptation:
- add config-driven selection, default to local runner
- enable OpenSandbox only when required env/config is present

Recommended toggle:
- `CodeMode:Runner = local|opensandbox` (default `local`)

### 4. Add OpenSandbox config only (no API surface changes)

Needed settings (from SDK usage):
- `OpenSandbox:Domain`
- `OpenSandbox:ApiKey`
- `OpenSandbox:Image` (default `ubuntu`)
- `OpenSandbox:TimeoutSeconds` (execution TTL)

Optional hardening:
- `OpenSandbox:RequestTimeoutSeconds`
- `OpenSandbox:UseServerProxy`

### 4.1 Local hosting expectation (Aspire AppHost default)

For this repository, local OpenSandbox testing is standardized on Aspire AppHost-managed container startup.

Why this is still minimum-change:
- application code remains unchanged except runner selection in DI
- OpenSandbox infrastructure is managed by AppHost instead of external compose workflow
- local developer setup is reproducible across machines

Recommended local values:
- `OpenSandbox:Domain = localhost:8080` (or mapped host port)
- `OpenSandbox:ApiKey` can be empty if local server auth is not enabled

If local auth is enabled:
- keep same domain
- set `OpenSandbox:ApiKey` to local token/key

Minimal AppHost shape (example):
- Add `opensandbox-server` container via `builder.AddContainer(...)`
- Expose `8080` endpoint via `WithEndpoint(...)`
- Configure `mcp-server` env: `OpenSandbox__Domain=localhost:8080`

Repository policy for local testing:
- `src/AppHost/Program.cs` must include the OpenSandbox container component
- local OpenSandbox validation should use `dotnet run --project src/AppHost/AppHost.csproj`

Operational note:
- set `CodeMode:Runner=opensandbox` only when AppHost-managed OpenSandbox is up
- keep `local` runner as default fallback for developer machines without docker

### 5. Keep trace model unchanged

Reuse existing ActivitySource names and tags:
- `mcp.code`
- `mcp.execute.callCount`
- timeout/call-limit tags

Only add OpenSandbox-specific tags if needed:
- `mcp.sandbox.provider = opensandbox`
- `mcp.sandbox.image`

### 6. Keep fallback path to local runner

If OpenSandbox setup fails (missing config, API error):
- fail fast during startup when runner=opensandbox
- do not silently switch in-request

This keeps production behavior deterministic and easier to debug.

## Exact File Delta (Smallest Practical Set)

1. Add new file
- `src/McpServer/CodeMode/OpenSandboxRunner.cs`

2. Update one existing file
- `src/McpServer/Program.cs`
	- add config-based runner selection
	- register either `LocalConstrainedRunner` or `OpenSandboxRunner`

3. Required local infra/docs updates
- `README.md` (short OpenSandbox setup section)
- appsettings/env var notes
- `src/AppHost/Program.cs` OpenSandbox component block

Optional fallback:
- `deploy/docker/docker-compose.opensandbox.yml` can remain as a manual fallback path

No required changes to:
- `src/McpServer/CodeMode/ExecuteTool.cs`
- `src/McpServer/CodeMode/ISandboxRunner.cs`
- `src/McpServer/CodeMode/WorkflowCoordinator.cs`
- search/meta tool handlers

## FastMCP Parity: What To Keep vs Defer

Keep now (high ROI, low risk):
1. Current synthetic surface and progressive discovery flow.
2. Strict execute guidance and constrained execution semantics.
3. Deterministic call bridge behavior.

Defer (avoid over-engineering in this phase):
1. Full transform framework split.
2. Multi-provider runtime orchestration.
3. Advanced discovery tools (`get_tags`, `list_tools`) unless prompt quality requires them.
4. Search engine replacement to BM25 unless benchmark data justifies it.

## Risks and Guardrails (OpenSandbox-Specific)

1. SDK maturity risk
- Current NuGet is alpha; pin exact version and avoid broad updates.

2. Runtime dependency risk
- OpenSandbox service must be available; validate connectivity at startup when enabled.

3. Timeout mismatch risk
- Keep clear separation:
	- MCP request cancellation token
	- OpenSandbox command timeout
	- sandbox TTL (`TimeoutSeconds`)

4. Cleanup risk
- Use `await using` and explicit `KillAsync()` where appropriate to avoid leaked sandboxes.

## Acceptance Criteria For This Adaptation

1. Switching `CodeMode:Runner=opensandbox` requires no client-facing API changes.
2. Existing execute prompts continue to work without prompt rewrites.
3. All current execute unit tests pass with local runner unchanged.
4. Added OpenSandbox runner tests cover success, timeout, and sandbox/API failure paths.
5. Observability still shows code text and per-run call counts.

## Bottom Line

If the objective is minimum change, OpenSandbox should be added as a new `ISandboxRunner` implementation plus DI toggle, not as an architectural rewrite.

This gives you reversible rollout, preserves current behavior, and keeps future FastMCP parity work independent from sandbox-provider migration.
