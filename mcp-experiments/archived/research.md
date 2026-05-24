# FastMCP Gap Analysis (Tool Search + Code Mode)

Date: 2026-03-20  
Scope: only tool-search and code-mode features. Other FastMCP features are intentionally ignored.

## FastMCP Baseline (source-verified)

From FastMCP docs, source, and tests:
- Tool-search transform exposes synthetic `search_tools` + `call_tool` and blocks recursive synthetic dispatch.
- CodeMode default discovery surface is `search` + `get_schema`; `execute` is always present.
- CodeMode flow is progressive: `search` -> `get_schema` -> `execute(code)`.
- Detail levels are `brief`, `detailed`, `full` across discovery tools.
- Defaults are `search=brief`, `get_schema=detailed`.
- `detailed` renders compact markdown parameters; `full` returns complete JSON schema payload.
- `search` supports optional `tags`, optional `limit`, and subset annotation (`N of M tools`) for non-full output.
- `execute` accepts Python code text, injects only `call_tool(name, params)`, and returns final result.

## Current Codebase Snapshot

Validated from implementation:
- Tool-search meta-surface exists with `search_tools` and `call_tool`.
- Recursive synthetic calls are blocked.
- Real tools are hidden behind internal registry and invoked through proxy handlers.
- Code-mode discovery exists with `search` and `get_schema`.
- Execute currently accepts step-array JSON (`execute(steps)`), not code text.
- Detail levels currently only `Brief` and `Detailed`.
- Current `get_schema` behavior:
  - `Brief` -> name-only JSON stub.
  - `Detailed` -> full JSON schema.
- Current `search` does not support `tags` filter or subset annotation.

## Confirmed Gaps (what must change)

1. Execute contract mismatch
- Target: `execute(code)` with sandboxed code and `call_tool` bridge.
- Current: `execute(steps)` plan execution.

2. Discovery detail model mismatch
- Target: `Brief`, `Detailed`, `Full` semantics aligned to FastMCP.
- Current: no `Full`; `Detailed` currently maps to full JSON.

3. `get_schema` output mismatch
- Target:
  - `Detailed` => compact markdown parameters.
  - `Full` => JSON schema.
- Current:
  - `Detailed` => JSON schema.

4. Search narrowing/coverage mismatch
- Target: `tags` filtering + `limit` handling + `N of M tools` hint (non-full output).
- Current: missing `tags` and subset annotation.

5. Surface-shape decision still open
- Current server exposes both tool-search and code-mode synthetic tools at once.
- FastMCP treats these as separate transforms.
- Decision needed: keep combined surface as intentional deviation, or provide selectable mode surfaces.

## Surface Policy Decision

- Chosen policy: keep combined synthetic surface (tool-search + code-mode together).
- Reason: aligns with current architecture where synthetic tools are intentionally exposed while real tools remain hidden in the registry.
- Validation: `list_tools` behavior is covered by unit tests to ensure only pinned + synthetic tools are visible.

## Intentional Deviation To Keep

- Keep deterministic weighted search instead of BM25.
- Reason: simpler implementation and predictable scoring; acceptable unless strict BM25 parity is explicitly required.

## Minimum Acceptance Criteria

- `execute` accepts `code` string and returns final value only.
- Runtime execution scope exposes only `call_tool` bridge.
- `SchemaDetailLevel` includes `Full`.
- `search` supports `detail`, optional `tags`, and subset annotation.
- `get_schema` default is `Detailed`; `Detailed` returns compact markdown; `Full` returns JSON schema.
