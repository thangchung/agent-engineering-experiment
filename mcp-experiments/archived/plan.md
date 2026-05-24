# Plan (Simple, Focused)

## Goal

Close only the gaps between this repo and FastMCP behavior for:
- tool-search transform behavior
- code-mode discovery and execute behavior

No work on other FastMCP features.

## Keep As-Is

1. Keep internal weighted search implementation (no BM25 package).
2. Keep tool registry and hidden real-tool design.
3. Keep recursion protection for synthetic tools.

## Phase 1: Execute Contract Parity (Highest)

### Changes

1. Replace public `execute(steps)` contract with `execute(code)`.
2. Update runner abstraction from plan execution to code execution.
3. Keep timeout and max-tool-call policies enforced in runtime.
4. Expose only `call_tool` bridge in execute scope.
5. Keep final-value-only response shape.

### Done Criteria

1. Unit tests prove `execute(code)` works with chained `await call_tool(...)`.
2. Unit tests prove only final value is returned.
3. Unit tests prove timeout and max-call limits still hold.

## Phase 2: Discovery Detail Parity

### Changes

1. Extend detail levels to `Brief`, `Detailed`, `Full`.
2. Update code-mode `search`:
- add optional `detail` parameter (default `Brief`)
- add optional `tags` filter
- add optional `limit` override
- add subset annotation (`N of M tools`) for non-full output
3. Update `get_schema`:
- default detail `Detailed`
- `Detailed` returns compact markdown parameter docs
- `Full` returns full JSON schema
- preserve partial-match behavior and report missing tool names

### Done Criteria

1. New unit tests pass for detail defaults and output formats.
2. Unit tests pass for tags filtering and subset annotation.
3. Existing discovery tests remain green.

## Phase 3: Surface Decision (Required)

### Changes

1. Decide whether to:
- keep combined synthetic surface (tool-search + code-mode together), or
- expose selectable mode surfaces for parity with FastMCP transform behavior.
2. Document the chosen behavior in README/research notes.

### Decision

1. Keep combined synthetic surface (current style).
2. Rationale: preserves existing client expectations in this repo and keeps discovery + execution flows available without mode toggles.
3. Guardrail: `list_tools` remains constrained to pinned + synthetic tools, validated by unit tests.

### Done Criteria

1. One mode policy selected and documented.
2. Tests validate `list_tools` output for chosen mode.

## Phase 4: Optional Extras (Not Required for Now)

1. Optional `get_tags` discovery tool.
2. Optional code-mode `list_tools` discovery tool.

## Test Commands

1. `dotnet test --filter "FullyQualifiedName~MetaTools|FullyQualifiedName~Search|FullyQualifiedName~DiscoveryTools|FullyQualifiedName~Execute"`
2. `dotnet test McpExperiments.slnx`

## Risks

1. Breaking clients currently using `execute(steps)`.
2. Markdown schema format for `Detailed` must stay deterministic for tests.
3. Ambiguity if combined-vs-selectable synthetic surface is not resolved.

## Mitigation

1. Add compatibility wrapper only if needed by active clients.
2. Snapshot-like tests for detailed schema markdown format.
3. Lock selected surface mode in tests and docs.
