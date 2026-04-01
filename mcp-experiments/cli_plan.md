# CLI Mode Implementation Plan

## Overview
Add dual-mode operation: HTTP MCP server (default) + local CLI mode (new). Shared DI layer, config-driven dispatch. Based on coffeeshop-cli patterns.

## Mode Selection
- **Config flag:** `Hosting:EnableCliMode` (env: `Hosting__EnableCliMode`, default: `false`)
- **Startup:** Check flag in `Program.cs` → dispatch to `RunServerMode()` or `RunCliMode()`
- **Behavior:** HTTP path unchanged; CLI is additive

## Phase 1: Foundation (1 week)
**Goal:** Dual-mode skeleton with tool discovery.

| Task | Files | Output | Acceptance Criteria |
|------|-------|--------|---|
| Update config schema | [appsettings.json](appsettings.json), [CliConfig.cs](#new) | Add `Hosting:EnableCliMode`, `Hosting:CliServeMode` | Config loads correctly with both flags |
| Add Spectre.Console NuGet | [McpServer.csproj](src/McpServer/McpServer.csproj) | Dependencies: Spectre.Console, Spectre.Console.Cli | Build succeeds |
| Split startup logic | [Program.cs](src/McpServer/Program.cs) | Add `RunServerMode()`, `RunCliMode()` methods | HTTP path works unchanged; CLI mode compiles |
| CLI command scaffold | [src/McpServer/Cli/](#new) | Empty command tree: `Commands/`, `ToolsListCommand.cs`, `ToolsShowCommand.cs`, `ServeCommand.cs` | `dotnet run -- --help` shows commands |
| Expose tools via discovery | [DiscoveryTools.cs](src/McpServer/CodeMode/DiscoveryTools.cs) | Reuse registry; create CLI adapter for `Name`, `Description`, `Tags`, `Schema` | `tools list` returns all registered tools |

**Tests:**
```csharp
[Fact] ConfigLoader_EnableCliMode_True() { ... }
[Fact] Program_CliMode_ExitCode0() { ... }
[Fact] ToolsListCommand_RendersTui() { ... }
```

---

## Phase 2: Query Command (1 week)
**Goal:** Execute tools locally from CLI.

| Task | Files | Output | Acceptance Criteria |
|------|-------|--------|---|
| Query command | [Cli/QueryCommand.cs](#new) | Handler for `query <intent>` or `query --tool <name> --args <json>` | `query "breweries in seattle"` works |
| Tool invocation layer | [Cli/ToolInvoker.cs](#new) | Routes to existing `MetaTools.CallToolAsync()` | Errors mapped to exit codes |
| Machine output | [Cli/JsonFormatter.cs](#new) | Both TUI and `--json` output paths | `query --json` returns structured result |
| Error mapping | [Cli/CliErrorHandler.cs](#new) | Catch tool not found, invalid args, execution errors | Errors render as human or JSON per mode |

**Tests:**
```csharp
[Fact] QueryCommand_ValidTool_Success() { ... }
[Fact] QueryCommand_InvalidTool_ExitCode1() { ... }
[Fact] QueryCommand_JsonOutput_ValidJson() { ... }
```

---

## Phase 3: Serve Mode (1 week)
**Goal:** CLI as stdio MCP bridge for local agent attachment.

| Task | Files | Output | Acceptance Criteria |
|------|-------|--------|---|
| Serve command | [Cli/ServeCommand.cs](#new) | `serve` subcommand runs MCP stdio loop | `dotnet run -- serve` listens on stdin/stdout |
| Stdio transport | [Cli/Mcp/StdioMcpBridge.cs](#new) | JSON-RPC handler: `initialize`, `tools/list`, `tools/call` | Agent can connect and invoke tools |
| Reuse tool handlers | [Tools/McpToolHandlers.cs](src/McpServer/Tools/McpToolHandlers.cs) | CLI serve and HTTP server use same handlers | Tool behavior identical in both modes |
| Connection docs | [README.md](README.md) (new section) | "CLI + Serve Mode" with examples | Users understand `stdio` attach workflow |

**Tests:**
```csharp
[Fact] ServeCommand_InitializeRequest_Responds() { ... }
[Fact] ServeCommand_ToolsListRequest_RespondsCatalog() { ... }
[Fact] StdioMcpBridge_JsonRpc_RoundTrip() { ... }
```

---

## Phase 4: Parity & Hardening (1 week)
**Goal:** Assert CLI ≈ HTTP server; no regressions.

| Task | Files | Output | Acceptance Criteria |
|------|-------|--------|---|
| Parity tests | [Tests/CliParity*.cs](#new) | Side-by-side assertions: tool count, schema format, error codes | CLI list == HTTP list count; schemas match |
| Startup perf | (existing suite) | Measure `dotnet run -- tools list` latency | Cold start ≤ 500ms |
| Integration suite | [Tests/CliIntegration*.cs](#new) | Real brewery API calls; both TUI and JSON paths | 95%+ test pass rate |
| Code review docs | [ARCHITECTURE.md](#new) | Dual-mode design, shared vs. divergent paths | Design decisions documented |

**Tests:**
```csharp
[Fact] CliTools_Count_EqualsHttpTools_Count() { ... }
[Fact] CliQuery_ToolInvocation_EqualsHttpCall_Results() { ... }
[Fact] CliStartup_Latency_LessThan500ms() { ... }
```

---

## Phase 5: Polish (1 week)
**Goal:** Docs, edge cases, no compiler warnings.

| Task | Files | Output | Acceptance Criteria |
|------|-------|--------|---|
| CLI guide | [CLI_GUIDE.md](#new) | End-to-end: discover → query → errors | 5+ concrete examples |
| Help text | All CLI commands | `--help` on each command; `global --help` | Help text populated for all commands |
| Edge cases | (existing + new tests) | Malformed JSON, missing tool, timeout | 90%+ line coverage on new Cli code |
| Format + lint | (existing suite) | Clean build, `dotnet format --verify` | Zero warnings; pass code review |

**Tests:**
- Existing full suite passes
- New CLI tests: ≥12 unit, ≥4 integration, 0 flaky
- Build: `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings

---

## File Structure (New Files)
```
src/McpServer/
├── Cli/                              # NEW
│   ├── Commands/
│   │   ├── ToolsListCommand.cs
│   │   ├── ToolsShowCommand.cs
│   │   ├── QueryCommand.cs
│   │   └── ServeCommand.cs
│   ├── CliConfig.cs                  # NEW
│   ├── CliErrorHandler.cs            # NEW
│   ├── ToolInvoker.cs                # NEW
│   ├── JsonFormatter.cs              # NEW
│   └── Mcp/
│       └── StdioMcpBridge.cs         # NEW
tests/McpServer.UnitTests/
├── Cli/                              # NEW
│   ├── CliConfigTests.cs
│   ├── ToolsListCommandTests.cs
│   ├── QueryCommandTests.cs
│   └── ServeCommandTests.cs
tests/McpServer.IntegrationTests/      # NEW if not exists
├── CliParity.cs
└── CliIntegration.cs
```

---

## Key Design Choices
1. **Reuse, don't duplicate:** CLI routes to existing `MetaTools` handlers → single source of truth.
2. **Config-driven:** Mode switch via `Hosting:EnableCliMode`; no code changes needed.
3. **Spectre.Console only:** No TUI outside CLI; HTTP server unaffected.
4. **Shared error handling:** `CliErrorHandler` maps tool errors to exit codes + JSON.
5. **Stdio bridge:** `serve` mode speaks JSON-RPC; agents attach via subprocess.

---

## Risks & Mitigations
| Risk | Mitigation |
|------|-----------|
| Spectre.Console adds 1.5MB binary | Document trade-off; conditional compilation optional (not planned) |
| CLI startup slower than HTTP server | Measure Phase 4; optimize if >500ms (cache discovery, lazy load) |
| Tool behavior diverges between modes | Parity tests in Phase 4; shared `MetaTools` layer enforces consistency |
| Test maintenance burden | 16+ CLI tests; place in separate `Cli/` folder; use test fixtures for DRY |
| Config chaos (CLI vs. HTTP flags conflict) | Namespace cleanly: all CLI flags under `Hosting:Cli*` |

---

## Success Criteria (Gating)
- [x] Phase 1: Build succeeds; HTTP server works unchanged; CLI scaffold loads.
- [x] Phase 2: `query` command invokes tools; errors map correctly.
- [ ] Phase 3: `serve` mode dialogue works over stdio; agent can connect.
- [ ] Phase 4: Parity tests pass; startup <500ms; 95%+ test coverage.
- [ ] Phase 5: No compiler warnings; docs complete; 0 flaky tests.

## Task Completion Checklist (31 Items)

Progress: `17/31` completed
Source: `cli_tasks.md` -> `Progress Tracker (Source of Truth)`
Last synced: `2026-03-27`

### Phase 1
- [x] T1.1 Add NuGet dependencies
- [x] T1.2 Create CliConfig record
- [x] T1.3 Create ConfigLoader
- [x] T1.4 Register CLI services in DI
- [x] T1.5 Split startup logic
- [x] T1.6 Create command base infrastructure
- [x] T1.7 Scaffold ToolsListCommand
- [x] T1.8 Scaffold ToolsShowCommand
- [x] T1.9 Scaffold ServeCommand (stub)
- [x] T1.10 Wire Spectre app in `RunCliModeAsync`
- [x] T1.11 Phase 1 quality gate

### Phase 2
- [x] T2.1 Create CliErrorHandler
- [x] T2.2 Create ToolInvoker
- [x] T2.3 Create JsonFormatter
- [x] T2.4 Create QueryCommand
- [x] T2.5 Register QueryCommand in CLI app
- [x] T2.6 Phase 2 quality gate

### Phase 3
- [ ] T3.1 Create StdioMcpBridge
- [ ] T3.2 Implement ServeCommand (real)
- [ ] T3.3 Document CLI + Serve mode in README
- [ ] T3.4 Phase 3 quality gate

### Phase 4
- [ ] T4.1 Create parity test suite
- [ ] T4.2 Create integration test suite
- [ ] T4.3 Performance baseline
- [ ] T4.4 Document architecture decisions
- [ ] T4.5 Phase 4 quality gate

### Phase 5
- [ ] T5.1 Create CLI_GUIDE.md
- [ ] T5.2 Populate `--help` text
- [ ] T5.3 Add edge case tests
- [ ] T5.4 Final quality gate (no warnings)
- [ ] T5.5 Final checklist and approval

---

## Commands Reference (After Completion)
```bash
# List tools
dotnet run -- tools list
dotnet run -- tools list --json

# Show tool schema
dotnet run -- tools show search_tools
dotnet run -- tools show brewery_search --json

# Query tools
dotnet run -- query "find breweries in seattle"
dotnet run -- query --tool brewery_search --args '{"city":"seattle"}' --json

# Serve mode (for agent attachment)
dotnet run -- serve

# HTTP server (unchanged)
dotnet run --project src/AppHost/AppHost.csproj  # No CLI; HTTP default
```

---

## Estimated Effort
- **Phase 1 (Foundation):** 3–4 days (~24h)
- **Phase 2 (Query):** 3–4 days (~24h)
- **Phase 3 (Serve):** 3–4 days (~20h)
- **Phase 4 (Parity):** 2–3 days (~16h)
- **Phase 5 (Polish):** 2 days (~12h)
- **Buffer:** 1 week (~40h)
- **Total:** ~5 weeks (210h solo, or 3 weeks parallel)

---

## Dependencies
- NuGet: `Spectre.Console` (v0.49+), `Spectre.Console.Cli` (v0.49+)
- Existing: `ModelContextProtocol`, `YamlDotNet` (if skills added later)
- .NET 10.0+
