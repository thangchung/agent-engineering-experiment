# Tasks (Tool Search + Code Mode Only)

## A. Execute Contract Parity (Highest)

- [x] Change MCP handler from `execute(steps)` to `execute(code)`.
- [x] Refactor runner contract from step plan to code text input.
- [x] Inject only `call_tool` bridge into runtime scope.
- [x] Keep timeout and max-call policy enforcement.
- [x] Keep response as final value only.

## B. Discovery Contract Parity

- [x] Add `Full` to `SchemaDetailLevel`.
- [x] Update `search` API shape to support:
  - [x] optional `detail` (default `Brief`)
  - [x] optional `tags`
  - [x] optional `limit`
  - [x] subset annotation (`N of M tools`) for non-full output
- [x] Update `get_schema` behavior:
  - [x] default to `Detailed`
  - [x] `Detailed` returns compact markdown params
  - [x] `Full` returns full JSON schema
  - [x] missing tools are reported alongside matched results

## C. Surface Mode Decision

- [x] Choose one surface policy:
  - [x] combined synthetic surface (current style), or
  - [ ] selectable tool-search/code-mode surfaces (FastMCP-like)
- [x] Add/adjust tests for `list_tools` output of chosen policy.
- [x] Document chosen policy in research/plan notes.

## D. Tests (Write First)

- [x] Add discovery tests:
  - [x] search default detail is brief
  - [x] get_schema default detail is detailed
  - [x] full detail returns full schema
  - [x] tags filter works
  - [x] subset annotation is present when results are truncated
- [x] Add execute tests:
  - [x] execute accepts code input
  - [x] code can call `await call_tool(...)`
  - [x] intermediate values are hidden
  - [x] timeout and call limits are enforced

## E. Validation

- [x] `dotnet test tests/McpServer.UnitTests/McpServer.UnitTests.csproj --filter "FullyQualifiedName~ExecuteAndRunnerTests"`
- [x] `dotnet test tests/McpServer.UnitTests/McpServer.UnitTests.csproj`
- [x] `dotnet test --filter "FullyQualifiedName~MetaTools|FullyQualifiedName~Search|FullyQualifiedName~DiscoveryTools|FullyQualifiedName~Execute"`
- [x] `dotnet test McpExperiments.slnx`

## F. Out Of Scope (for now)

- [x] Do not add BM25/external search library.
- [x] Do not add non-essential FastMCP features.
- [x] Do not add `get_tags` or code-mode `list_tools` unless explicitly requested.
