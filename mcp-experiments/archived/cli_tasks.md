# CLI Mode Implementation Tasks

## Principles
- **KISS:** Every task ≤ 2 hours, atomic, focused.
- **YAGNI:** No optional features; only what´s in plan_cli.md.
- **DRY:** Consolidate patterns; reuse test fixtures and common error handling.
- **Boy-Scout:** Every deliverable includes format check, linting, unit tests.

## Progress Tracker (Source of Truth)

Progress: `17/31` completed
- [x] T1.1
- [x] T1.2
- [x] T1.3
- [x] T1.4
- [x] T1.5
- [x] T1.6
- [x] T1.7
- [x] T1.8
- [x] T1.9
- [x] T1.10
- [x] T1.11
- [x] T2.1
- [x] T2.2
- [x] T2.3
- [x] T2.4
- [x] T2.5
- [x] T2.6
- [ ] T3.1
- [ ] T3.2
- [ ] T3.3
- [ ] T3.4
- [ ] T4.1
- [ ] T4.2
- [ ] T4.3
- [ ] T4.4
- [ ] T4.5
- [ ] T5.1
- [ ] T5.2
- [ ] T5.3
- [ ] T5.4
- [ ] T5.5

Tracker rule:
- You can check tasks in either `cli_tasks.md` or `cli_plan.md`.

---

## Phase 1: Foundation (5 tasks + quality gate)

### T1.1: Add NuGet Dependencies
- **Title:** Update McpServer.csproj with Spectre.Console packages
- **Description:** Add `Spectre.Console` and `Spectre.Console.Cli` to enable TUI rendering and CLI app framework.
- **Files:** `src/McpServer/McpServer.csproj`
- **Deliverable:** 
  - Add `<PackageReference Include="Spectre.Console" Version="0.49.0" />`
  - Add `<PackageReference Include="Spectre.Console.Cli" Version="0.49.0" />`
- **Acceptance Criteria:** 
  - `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings
  - `dotnet restore` downloads packages without errors
- **Effort:** 15 min
- **Dependencies:** None

### T1.2: Create CLI Config Record
- **Title:** Define CliConfig record with mode flags
- **Description:** Create immutable configuration record for `Hosting:EnableCliMode` and `Hosting:CliServeMode` flags as per config schema.
- **Files:** `src/McpServer/Cli/CliConfig.cs` (NEW)
- **Deliverable:**
  ```csharp
  public record CliConfig(
      bool EnableCliMode = false,
      bool CliServeMode = false);
  ```
- **Acceptance Criteria:**
  - Record compiles; properties accessible
  - Can be initialized from `IConfiguration`
- **Effort:** 20 min
- **Dependencies:** T1.1

### T1.3: Create Config Loader
- **Title:** Build ConfigLoader to bind CliConfig from appsettings.json and environment variables
- **Description:** Reuse coffeeshop-cli precedence pattern: env vars override JSON; defaults applied. No external dependencies (YamlDotNet not needed yet).
- **Files:** `src/McpServer/Cli/ConfigLoader.cs` (NEW)
- **Deliverable:**
  - Static method `LoadCliConfigAsync(IConfiguration cfg)` → `CliConfig`
  - Env var binding: `Hosting__EnableCliMode` → `EnableCliMode`
  - Default: `EnableCliMode = false`
- **Acceptance Criteria:**
  - Config binds from appsettings.json (add entries to `appsettings.json`)
  - Env var precedence verified (set `Hosting__EnableCliMode=true` and assert it takes precedence)
  - Unit test passes
- **Effort:** 45 min
- **Dependencies:** T1.2
- **Quality Gate:** Unit test `ConfigLoader_EnableCliMode_Default_False`, `ConfigLoader_EnvVar_Override_JsonSetting`

### T1.4: Register CLI Services in DI
- **Title:** Add CLI-specific DI registrations in Program.cs
- **Description:** Register `ICommandApp` and CLI service abstractions. Keep HTTP path unchanged; branch via config flag.
- **Files:** `src/McpServer/Program.cs` (MODIFY)
- **Deliverable:**
  - Create `void RegisterCliServices(IServiceCollection services)` extension
  - Register placeholder `ICommandApp` (real app build happens at startup, not DI layer)
  - Load `CliConfig` into DI
- **Acceptance Criteria:**
  - DI registration compiles
  - `GetRequiredService<CliConfig>` works in CLI mode
  - HTTP startup flow unaffected (test runs existing HTTP test)
- **Effort:** 30 min
- **Dependencies:** T1.3
- **Quality Gate:** Existing HTTP integration test still passes

### T1.5: Split Startup Logic (Server vs. CLI)
- **Title:** Refactor Program.cs to dispatch based on EnableCliMode flag
- **Description:** Extract existing HTTP server logic to `RunServerModeAsync()`, create `RunCliModeAsync()` stub. Main entry point checks flag and routes.
- **Files:** `src/McpServer/Program.cs` (MODIFY)
- **Deliverable:**
  - Extract HTTP startup → `static Task RunServerModeAsync(WebApplicationBuilder builder, CliConfig cliConfig)`
  - Create stub → `static Task RunCliModeAsync(IServiceProvider sp, CliConfig cliConfig)` (returns completed task)
  - Main `await` branch: `if (cliConfig.EnableCliMode) await RunCliModeAsync(...) else await RunServerModeAsync(...)`
- **Acceptance Criteria:**
  - `dotnet run` (default, `EnableCliMode=false`) → HTTP server starts (verify on port 5000)
  - `Hosting__EnableCliMode=true dotnet run` → CLI mode runs (no HTTP port)
  - Build: 0 errors, 0 warnings
- **Effort:** 60 min
- **Dependencies:** T1.4
- **Quality Gate:** Existing HTTP test passes; new CLI mode initialization test passes

### T1.6: Create CLI Command Base Infrastructure
- **Title:** Create Spectre.Console.Cli command base and TypeRegistrar
- **Description:** Establish command framework: base command class, DI resolver for Spectre.Console. Minimal infrastructure, no business logic yet.
- **Files:** 
  - `src/McpServer/Cli/Commands/BaseCliCommand.cs` (NEW)
  - `src/McpServer/Cli/Infrastructure/TypeRegistrar.cs` (NEW)
  - `src/McpServer/Cli/Infrastructure/TypeResolver.cs` (NEW)
- **Deliverable:**
  - `BaseCliCommand : Command` with property for `IServiceProvider`
  - `TypeRegistrar : ITypeRegistrar` for DI integration (copied pattern from coffeeshop-cli, adapted)
  - `TypeResolver : ITypeResolver` for runtime resolution
- **Acceptance Criteria:**
  - Classes compile without errors
  - `TypeRegistrar` resolves a test service from DI (unit test)
  - `BaseCliCommand` injectable with `IServiceProvider`
- **Effort:** 45 min
- **Dependencies:** T1.5
- **Quality Gate:** Unit test `TypeRegistrar_Resolve_Success`, `BaseCliCommand_ServiceProvider_NotNull`

### T1.7: Scaffold ToolsListCommand
- **Title:** Create `tools list` command displaying all registered tools
- **Description:** Command inherits `BaseCliCommand`, queries registry via `IToolRegistry.GetAllAsync()`, renders as TUI table (Spectre.Console). No JSON output yet.
- **Files:** `src/McpServer/Cli/Commands/ToolsListCommand.cs` (NEW)
- **Deliverable:**
  - `ToolsListCommand : BaseCliCommand`
  - Executes `ToolRegistry.GetAllAsync()` in `ExecuteAsync`
  - Renders Spectre `Table` with columns: Tool Name, Description
  - Returns exit code 0 on success
- **Acceptance Criteria:**
  - `dotnet run -- tools list` displays table (manual test in CLI mode)
  - TUI table renders without throwing
  - Exit code = 0
  - Unit test mocks `IToolRegistry` and asserts table rendering
- **Effort:** 60 min
- **Dependencies:** T1.6
- **Quality Gate:** Unit test `ToolsListCommand_RendersTui_Success`

### T1.8: Scaffold ToolsShowCommand
- **Title:** Create `tools show <name>` command displaying tool schema
- **Description:** Command takes tool name as argument, queries registry, displays full schema (parameters, description). Reuses registry, minimal logic.
- **Files:** `src/McpServer/Cli/Commands/ToolsShowCommand.cs` (NEW)
- **Deliverable:**
  - `ToolsShowCommand : BaseCliCommand<ToolsShowSettings>` where `ToolsShowSettings` has `ToolName : string` argument
  - Executes `ToolRegistry.FindByNameAsync(toolName)`
  - Renders schema as JSON or formatted table
  - Returns exit code 1 if tool not found
- **Acceptance Criteria:**
  - `dotnet run -- tools show search_tools` displays schema (manual test)
  - Missing tool → exit code 1, error message
  - Unit test mocks registry and verifies output
- **Effort:** 60 min
- **Dependencies:** T1.7
- **Quality Gate:** Unit test `ToolsShowCommand_ValidTool_Success`, `ToolsShowCommand_MissingTool_ExitCode1`

### T1.9: Scaffold ServeCommand (Stub)
- **Title:** Create `serve` command (stub for Phase 3)
- **Description:** Minimal command that returns exit code 0. Placeholder for Phase 3 stdio loop. Prevents `dotnet run -- serve` from failing.
- **Files:** `src/McpServer/Cli/Commands/ServeCommand.cs` (NEW)
- **Deliverable:**
  - `ServeCommand : BaseCliCommand`
  - `ExecuteAsync` returns 0 (stub)
  - Placeholder message: "Serve mode not implemented yet"
- **Acceptance Criteria:**
  - `dotnet run -- serve` runs without error, exits 0
  - Unit test verifies exit code
- **Effort:** 15 min
- **Dependencies:** T1.8
- **Quality Gate:** Unit test `ServeCommand_Stub_ExitCode0`

### T1.10: Wire Spectre.Console.Cli App in RunCliModeAsync
- **Title:** Build and run Spectre CommandApp in RunCliModeAsync
- **Description:** Instantiate Spectre `CommandApp` with DI-integrated TypeRegistrar, register ToolsListCommand, ToolsShowCommand, ServeCommand. Main entry point for CLI mode.
- **Files:** `src/McpServer/Program.cs` (MODIFY)
- **Deliverable:**
  - In `RunCliModeAsync`: 
    - Create `ICommand rootCommand = new ToolsListCommand()`
    - Build `ICommandApp` using `CommandAppBuilder` with TypeRegistrar
    - Register commands: `tools list`, `tools show`, `serve`
    - Call `RunAsync(args)`
- **Acceptance Criteria:**
  - `Hosting__EnableCliMode=true dotnet run -- --help` shows all commands
  - `Hosting__EnableCliMode=true dotnet run -- tools list` displays tools
  - Build: 0 errors, 0 warnings
- **Effort:** 45 min
- **Dependencies:** T1.9
- **Quality Gate:** Manual verification; existing HTTP tests still pass

### T1.11: Final Build & Test (Phase 1 Gate)
- **Title:** Verify all Phase 1 deliverables compile, lint, run tests
- **Description:** Comprehensive quality check: build, format, lint, unit test suite. All tasks above must be complete before this.
- **Files:** (all modified/created in T1.1–T1.10)
- **Deliverable:**
  - Run `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings
  - Run `dotnet format --verify-no-changes` → no changes needed
  - Run `dotnet test McpExperiments.slnx --filter "FullyQualifiedName~Cli"` → all CLI unit tests pass
  - HTTP server mode test passes (regression check)
- **Acceptance Criteria:**
  - 0 build errors
  - 0 lint/format violations
  - ≥ 6 CLI unit tests passing (T1.2–T1.9)
  - Existing HTTP tests still pass (≥40 tests)
  - Total: ≥46 tests passing
- **Effort:** 30 min
- **Dependencies:** T1.10

---

## Phase 2: Query Command (4 tasks + quality gate)

### T2.1: Create CliErrorHandler
- **Title:** Centralize error mapping for CLI mode
- **Description:** Define error hierarchy and map tool exceptions to exit codes. Single source of truth for error handling; reused by commands.
- **Files:** `src/McpServer/Cli/CliErrorHandler.cs` (NEW)
- **Deliverable:**
  - `static class CliErrorHandler`
  - `static int MapExceptionToExitCode(Exception ex)` → maps tool errors to exit codes (0 success, 1 generic, 2 tool not found, etc.)
  - `static string FormatErrorMessage(Exception ex, bool verbose)` → human or JSON format
- **Acceptance Criteria:**
  - Tool not found → exit code 2
  - Invalid JSON → exit code 1
  - Generic error → exit code 1
  - Unit test verifies mapping
- **Effort:** 40 min
- **Dependencies:** T1.11 (Phase 1 complete)
- **Quality Gate:** Unit test `CliErrorHandler_ToolNotFound_ExitCode2`, `CliErrorHandler_InvalidJson_ExitCode1`

### T2.2: Create ToolInvoker
- **Title:** Wrapper layer to invoke tools from CLI
- **Description:** Routes to existing `MetaTools.CallToolAsync()`. Does NOT duplicate tool logic; delegates to existing handler. Single responsibility: argument parsing + invocation.
- **Files:** `src/McpServer/Cli/ToolInvoker.cs` (NEW)
- **Deliverable:**
  - `class ToolInvoker`
  - `async Task<ToolResult> InvokeAsync(string toolName, Dictionary<string, JsonElement> args)` → calls `MetaTools.CallToolAsync`
  - Returns structured result (success, error, output)
- **Acceptance Criteria:**
  - Invokes existing tool handler without duplication
  - Returns `ToolResult` with `IsSuccess`, `Output`, `Error` properties
  - Unit test mocks `MetaTools` and verifies invocation
- **Effort:** 40 min
- **Dependencies:** T2.1
- **Quality Gate:** Unit test `ToolInvoker_CallTool_Success`, `ToolInvoker_ToolNotFound_Error`

### T2.3: Create JsonFormatter
- **Title:** Handle TUI and JSON output formatting
- **Description:** Single formatter class with both paths. DRY: don´t duplicate output logic in commands. Returns formatted string (human or machine).
- **Files:** `src/McpServer/Cli/JsonFormatter.cs` (NEW)
- **Deliverable:**
  - `static class JsonFormatter`
  - `static string FormatToolResult(ToolResult result, bool asJson)` → human table or JSON
  - `static string FormatToolingOutput(JsonElement output, bool asJson)` → formats tool output
- **Acceptance Criteria:**
  - `asJson=false` → Spectre AnsiConsole render
  - `asJson=true` → compact JSON (no extra whitespace)
  - Unit test verifies both paths
- **Effort:** 45 min
- **Dependencies:** T2.2
- **Quality Gate:** Unit test `JsonFormatter_AsJson_ValidJson`, `JsonFormatter_AsTui_NoThrow`

### T2.4: Create QueryCommand
- **Title:** Implement `query <intent>` or `query --tool <name> --args <json>` command
- **Description:** Core CLI feature: invoke tools from command line. Accepts intent (natural language hint) or explicit tool+args. Uses `ToolInvoker`, delegates to existing handlers.
- **Files:** `src/McpServer/Cli/Commands/QueryCommand.cs` (NEW)
- **Deliverable:**
  - `QueryCommand : BaseCliCommand<QuerySettings>`
  - `QuerySettings`: `ToolName? : string`, `Args? : string`, `Intent : string`, `--json` flag
  - Parses `--args` as JSON, invokes via `ToolInvoker`
  - Formats output via `JsonFormatter`
  - Maps errors via `CliErrorHandler`
- **Acceptance Criteria:**
  - `dotnet run -- query --tool search_tools --args '{"query":"breweries"}'` works (manual test)
  - Missing tool → exit code 2
  - Invalid JSON → exit code 1
  - `--json` flag produces valid JSON output
  - Unit test verifies all paths (valid tool, invalid tool, invalid JSON)
- **Effort:** 90 min
- **Dependencies:** T2.3
- **Quality Gate:** Unit test `QueryCommand_ValidTool_Success`, `QueryCommand_InvalidTool_ExitCode2`, `QueryCommand_InvalidJson_ExitCode1`, `QueryCommand_JsonOutput_ValidJson`

### T2.5: Register QueryCommand in CLI App
- **Title:** Add `query` command to Spectre.Console.Cli app
- **Description:** One-line addition to `RunCliModeAsync` to register `QueryCommand` with default command priority.
- **Files:** `src/McpServer/Program.cs` (MODIFY)
- **Deliverable:**
  - In CLI app builder: `.AddCommand<QueryCommand>("query")`
- **Acceptance Criteria:**
  - `Hosting__EnableCliMode=true dotnet run -- query --help` shows command help
  - Command runs without throwing
- **Effort:** 10 min
- **Dependencies:** T2.4
- **Quality Gate:** Existing CLI commands still work

### T2.6: Final Build & Test (Phase 2 Gate)
- **Title:** Verify all Phase 2 deliverables compile, test, no regressions
- **Description:** Quality check: build, lint, new CLI tests, existing tests unchanged.
- **Files:** (all modified/created in T2.1–T2.5)
- **Deliverable:**
  - `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings
  - `dotnet test McpExperiments.slnx --filter "FullyQualifiedName~Cli"` → all CLI tests pass
  - Existing HTTP and unit tests pass (no regression)
- **Acceptance Criteria:**
  - ≥ 12 CLI unit tests passing (Phase 1 + Phase 2 combined)
  - ≥ 50 total tests passing (including existing)
  - Build clean, format clean
- **Effort:** 20 min
- **Dependencies:** T2.5

---

## Phase 3: Serve Mode (3 tasks + quality gate)

### T3.1: Create StdioMcpBridge
- **Title:** Implement JSON-RPC stdio transport for MCP protocol
- **Description:** Handles JSON-RPC over stdin/stdout. Reuses existing `MetaTools` handlers (search, call_tool, get_schema, execute). No new tool logic; pure transport.
- **Files:** `src/McpServer/Cli/Mcp/StdioMcpBridge.cs` (NEW)
- **Deliverable:**
  - `class StdioMcpBridge`
  - `async Task RunAsync(CancellationToken ct)` → reads JSON-RPC from stdin, dispatches, writes to stdout
  - Handles `initialize`, `tools/list_tools`, `tools/call_tool` requests
  - Maps tool errors to MCP error responses
- **Acceptance Criteria:**
  - Stdio loop reads and parses JSON-RPC frames
  - Sends `initialize` response with tools capability
  - Calls tools and returns results without throwing
  - Unit test mocks stdin/stdout and verifies RPC flow
- **Effort:** 120 min
- **Dependencies:** T2.6 (Phase 2 complete)
- **Quality Gate:** Unit test `StdioMcpBridge_InitializeRequest_Success`, `StdioMcpBridge_ToolsListRequest_Success`, `StdioMcpBridge_ToolsCallRequest_Success`

### T3.2: Create ServeCommand (Real Implementation)
- **Title:** Implement full `serve` command; replaces T1.9 stub
- **Description:** Command instantiates `StdioMcpBridge` and runs it. Keeps alive until signal or error. Minimal command logic; delegates to bridge.
- **Files:** `src/McpServer/Cli/Commands/ServeCommand.cs` (MODIFY from T1.9)
- **Deliverable:**
  - Update `ExecuteAsync` to create `StdioMcpBridge` and call `RunAsync`
  - Handle graceful shutdown on SIGTERM/SIGINT
  - Log startup message to stderr (not stdout, which is reserved for JSON-RPC)
- **Acceptance Criteria:**
  - `dotnet run -- serve` starts and listens on stdin
  - Can receive JSON-RPC frames and respond
  - CTRL+C terminates cleanly
  - Unit test verifies startup and shutdown
- **Effort:** 60 min
- **Dependencies:** T3.1
- **Quality Gate:** Unit test `ServeCommand_Startup_Success`, `ServeCommand_Shutdown_Graceful`

### T3.3: Document CLI + Serve Mode in README
- **Title:** Add section to README.md documenting CLI and serve mode usage
- **Description:** User-facing documentation with examples: discovery, query, serve, agent attachment.
- **Files:** `README.md` (MODIFY, add section)
- **Deliverable:**
  - Section "CLI Mode" with:
    - Quick start: `Hosting__EnableCliMode=true dotnet run`
    - Examples: `tools list`, `tools show`, `query`, `serve`
    - Agent attachment example: `stdio` subprocess
  - At least 3 code blocks showing real CLI commands
- **Acceptance Criteria:**
  - README includes "CLI" in TOC or heading
  - Examples are copy-pasteable and work
  - Serve mode attachment instructions clear
- **Effort:** 45 min
- **Dependencies:** T3.2
- **Quality Gate:** Manual review; docs build without errors

### T3.4: Final Build & Test (Phase 3 Gate)
- **Title:** Verify all Phase 3 deliverables compile, test, no regressions
- **Description:** Quality check: build, lint, new stdio tests, existing tests unchanged.
- **Files:** (all modified/created in T3.1–T3.3)
- **Deliverable:**
  - `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings
  - `dotnet test McpExperiments.slnx --filter "FullyQualifiedName~Cli"` → all CLI tests pass
  - Existing HTTP and phase 1–2 CLI tests pass (no regression)
- **Acceptance Criteria:**
  - ≥ 15 CLI unit tests passing (Phase 1 + 2 + 3 combined)
  - ≥ 53 total tests passing
  - Build clean, format clean
- **Effort:** 20 min
- **Dependencies:** T3.3

---

## Phase 4: Parity & Hardening (4 tasks + quality gate)

### T4.1: Create Parity Test Suite
- **Title:** Assert CLI tool discovery ≈ HTTP server tool list
- **Description:** Unit tests verify: same tool count, same schema format, same error codes. Prevents divergence between modes.
- **Files:** `tests/McpServer.UnitTests/Cli/CliParityTests.cs` (NEW)
- **Deliverable:**
  - `[Fact] CliTools_Count_EqualsHttpTools()` → CLI and HTTP registry have same count
  - `[Fact] CliToolsSchema_EqualsHttpSchema()` → schemas match exactly
  - `[Fact] CliErrorCodes_MatchHttpErrorCodes()` → same error mappings
- **Acceptance Criteria:**
  - All assertions pass
  - Tests run in ≤ 1 second (fast)
  - No external dependencies (mocked)
- **Effort:** 60 min
- **Dependencies:** T3.4 (Phase 3 complete)
- **Quality Gate:** Unit test `CliParityTests_All_Pass`

### T4.2: Create Integration Test Suite
- **Title:** Real brewery API calls; both CLI and HTTP paths
- **Description:** Tests that invoke the real brewery API via both modes. Verifies tool behavior identical end-to-end.
- **Files:** `tests/McpServer.IntegrationTests/CliIntegrationTests.cs` (NEW dir if needed)
- **Deliverable:**
  - `[Fact] CliQuery_BrewerySearch_Success()` → call real API, verify result
  - `[Fact] HttpQuery_BrewerySearch_Success()` → same call via HTTP, same result
  - `[Fact] CliServe_ReceiveToolCall_SameAsHttp()` → stdio bridge returns same result as HTTP
- **Acceptance Criteria:**
  - All ≥3 integration tests pass
  - Both modes return identical data (not just structure, but values)
  - Tests marked as `[Trait("Category", "Integration")]` or similar to allow skipping
- **Effort:** 90 min
- **Dependencies:** T4.1
- **Quality Gate:** Integration tests pass; can be run separately with `--filter "Category~Integration"`

### T4.3: Performance Baseline
- **Title:** Measure and document CLI startup latency
- **Description:** Record warm and cold startup times for `dotnet run -- tools list`. Gate: ≤ 500ms cold start. Baseline for future optimizations.
- **Files:** Performance data in PR description or ARCHITECTURE.md
- **Deliverable:**
  - Run `time Hosting__EnableCliMode=true dotnet run -- tools list` 5 times
  - Record cold start (first run) and warm start (subsequent runs)
  - Document in ARCHITECTURE.md table: startup benchmarks
- **Acceptance Criteria:**
  - Cold start ≤ 500ms
  - Warm start ≤ 200ms
  - Baseline documented
- **Effort:** 30 min
- **Dependencies:** T4.2
- **Quality Gate:** Acceptable latency, documented

### T4.4: Document Architecture Decisions
- **Title:** Create ARCHITECTURE.md with dual-mode design rationale
- **Description:** Explain: shared handler layer, config-driven dispatch, CLI-specific vs. shared code, tooling reuse. Aids code review and future maintenance.
- **Files:** `ARCHITECTURE.md` (NEW) or root-level
- **Deliverable:**
  - Section: "CLI Mode Architecture"
  - Diagram or table: HTTP flow vs. CLI flow (both route to `MetaTools`)
  - Key decisions: why reuse handlers, why Spectre.Console, why stdio bridge
  - Known limitations and trade-offs
- **Acceptance Criteria:**
  - Document is ≥ 3 sections
  - Rationale clear (reader understands design choices)
  - Includes 1+ diagram or flow chart
- **Effort:** 60 min
- **Dependencies:** T4.3
- **Quality Gate:** Manual review; document is clear and complete

### T4.5: Final Build & Test (Phase 4 Gate)
- **Title:** Verify all Phase 4 deliverables compile, test, no regressions
- **Description:** Quality check: unit + integration tests, parity assertions, performance gate, architecture docs.
- **Files:** (all modified/created in T4.1–T4.4)
- **Deliverable:**
  - `dotnet build McpExperiments.slnx` → 0 errors, 0 warnings
  - `dotnet test McpExperiments.slnx` → all unit tests pass (≥ 60 tests)
  - Integration tests pass (can be run separately)
  - Performance baseline met (≤ 500ms)
  - ARCHITECTURE.md reviewed and approved
- **Acceptance Criteria:**
  - ≥ 60 tests passing (including Phase 1–3 + parity)
  - 0 build errors/warnings
  - Parity tests all green
  - Performance acceptable
- **Effort:** 20 min
- **Dependencies:** T4.4

---

## Phase 5: Polish (4 tasks + final gate)

### T5.1: Create CLI_GUIDE.md
- **Title:** End-to-end user guide for CLI mode
- **Description:** How-to guide with ≥ 5 concrete examples: discover, query, JSON output, serve, agent attach.
- **Files:** `CLI_GUIDE.md` (NEW)
- **Deliverable:**
  - Example 1: `tools list` to discover all tools
  - Example 2: `tools show <tool>` to inspect schema
  - Example 3: `query --tool X --args {...}` to invoke
  - Example 4: `query --json` for scripting
  - Example 5: `serve` for agent attachment
  - Troubleshooting section
- **Acceptance Criteria:**
  - ≥ 5 examples, all tested and working
  - Copy-pasteable code blocks
  - Troubleshooting section addresses common issues
- **Effort:** 60 min
- **Dependencies:** T4.5 (Phase 4 complete)
- **Quality Gate:** Manual verification; guide is actionable

### T5.2: Populate --help Text on All Commands
- **Title:** Ensure every CLI command has comprehensive help text
- **Description:** Spectre.Console renders `--help` from command descriptions and settings. Verify all commands have [Description(...)] attributes.
- **Files:** 
  - `src/McpServer/Cli/Commands/*.cs` (all command files)
  - Verify each `Command` class and `*Settings` record have `[Description]` attributes
- **Deliverable:**
  - Every command and setting param has `[Description("...")]`
  - Run `dotnet run -- --help`, `dotnet run -- tools --help`, `dotnet run -- query --help` manually
  - Verify output is clear and complete
- **Acceptance Criteria:**
  - `dotnet run -- --help` shows all commands
  - `dotnet run -- <cmd> --help` shows options for each
  - No ambiguous or missing descriptions
- **Effort:** 40 min
- **Dependencies:** T5.1
- **Quality Gate:** Manual `--help` inspection; all output clear

### T5.3: Edge Case Tests
- **Title:** Add tests for CLI edge cases (malformed JSON, timeouts, missing args)
- **Description:** Coverage for realistic failure modes: bad JSON, tool timeout, missing required args, etc.
- **Files:** `tests/McpServer.UnitTests/Cli/CliEdgeCaseTests.cs` (NEW)
- **Deliverable:**
  - `[Fact] QueryCommand_MalformedJson_ExitCode1()`
  - `[Fact] ServeCommand_MalformedRpc_ContinuesListening()`
  - `[Fact] QueryCommand_MissingRequiredArg_ExitCode1()`
  - `[Fact] ToolsShowCommand_EmptyToolName_ExitCode1()`
- **Acceptance Criteria:**
  - ≥ 4 edge case tests
  - All pass
  - Coverage of CLI code is 90%+
- **Effort:** 60 min
- **Dependencies:** T5.2
- **Quality Gate:** Unit test edge case suite passes; coverage ≥ 90%

### T5.4: Final Quality Gate (No Warnings)
- **Title:** Compile, format, lint entire solution; zero warnings
- **Description:** Final comprehensive check: build clean, no style issues, no dead code, complete formatting.
- **Files:** (all files in McpExperiments.slnx)
- **Deliverable:**
  - `dotnet build McpExperiments.slnx` → 0 errors, **0 warnings**
  - `dotnet format --verify-no-changes` → no changes needed
  - `dotnet test McpExperiments.slnx` → all tests pass (≥ 65 tests)
- **Acceptance Criteria:**
  - Build output: "Build succeeded. 0 Warning(s)"
  - Format check: "No files need reformatting"
  - Test output: all tests green
  - Code review ready
- **Effort:** 20 min
- **Dependencies:** T5.3

### T5.5: Final Build & Approval (Phase 5 Gate)
- **Title:** Verify all deliverables ready for merge; final checklist
- **Description:** Last gate before PR merge: docs complete, tests comprehensive, no tech debt.
- **Files:** (all, comprehensive)
- **Deliverable:**
  - ✅ Build: 0 errors, 0 warnings
  - ✅ Tests: ≥ 65 passing (40 existing + 25 new CLI)
  - ✅ Docs: README updated, CLI_GUIDE.md complete, ARCHITECTURE.md documented
  - ✅ Code: `dotnet format` clean, no dead code, Boy-Scout (improved from initial state)
  - ✅ Parity: CLI and HTTP modes produce identical results
  - ✅ Performance: cold start ≤ 500ms
- **Acceptance Criteria:**
  - All 5 items above checked
  - PR description links to tasks T1.1–T5.5 (or summarizes completion)
  - No blockers for merge
- **Effort:** 30 min
- **Dependencies:** T5.4

---

## Summary Table

| Phase | Task ID | Task | Effort | Dependencies |
|-------|---------|------|--------|---|
| 1 | T1.1 | Add NuGet dependencies | 15 min | None |
| 1 | T1.2 | Create CliConfig record | 20 min | T1.1 |
| 1 | T1.3 | Create ConfigLoader | 45 min | T1.2 |
| 1 | T1.4 | Register CLI services in DI | 30 min | T1.3 |
| 1 | T1.5 | Split startup logic | 60 min | T1.4 |
| 1 | T1.6 | Create command base infrastructure | 45 min | T1.5 |
| 1 | T1.7 | Scaffold ToolsListCommand | 60 min | T1.6 |
| 1 | T1.8 | Scaffold ToolsShowCommand | 60 min | T1.7 |
| 1 | T1.9 | Scaffold ServeCommand (stub) | 15 min | T1.8 |
| 1 | T1.10 | Wire Spectre app in RunCliModeAsync | 45 min | T1.9 |
| 1 | T1.11 | Phase 1 quality gate | 30 min | T1.10 |
| 2 | T2.1 | Create CliErrorHandler | 40 min | T1.11 |
| 2 | T2.2 | Create ToolInvoker | 40 min | T2.1 |
| 2 | T2.3 | Create JsonFormatter | 45 min | T2.2 |
| 2 | T2.4 | Create QueryCommand | 90 min | T2.3 |
| 2 | T2.5 | Register QueryCommand in CLI app | 10 min | T2.4 |
| 2 | T2.6 | Phase 2 quality gate | 20 min | T2.5 |
| 3 | T3.1 | Create StdioMcpBridge | 120 min | T2.6 |
| 3 | T3.2 | Implement ServeCommand (real) | 60 min | T3.1 |
| 3 | T3.3 | Document CLI + Serve in README | 45 min | T3.2 |
| 3 | T3.4 | Phase 3 quality gate | 20 min | T3.3 |
| 4 | T4.1 | Create parity test suite | 60 min | T3.4 |
| 4 | T4.2 | Create integration test suite | 90 min | T4.1 |
| 4 | T4.3 | Performance baseline | 30 min | T4.2 |
| 4 | T4.4 | Document architecture decisions | 60 min | T4.3 |
| 4 | T4.5 | Phase 4 quality gate | 20 min | T4.4 |
| 5 | T5.1 | Create CLI_GUIDE.md | 60 min | T4.5 |
| 5 | T5.2 | Populate --help text | 40 min | T5.1 |
| 5 | T5.3 | Edge case tests | 60 min | T5.2 |
| 5 | T5.4 | Final quality gate | 20 min | T5.3 |
| 5 | T5.5 | Final checklist & approval | 30 min | T5.4 |
| | | **TOTAL** | **1,265 min (21 hrs)** | — |

**Realistic estimate (with interruptions, reviews, rework): 4–5 weeks for one developer.**

---

## Key Principles Applied

### KISS
- Each task ≤ 2 hours, atomic scope
- No mega-tasks; no "refactor all error handling" lumped together
- One responsibility per file

### YAGNI
- No optional features (e.g., no REPL, no watch mode, no config file parsing)
- Only tasks directly from plan_cli.md
- Placeholder stub (T1.9) replaced with real impl (T3.2), not duplicated

### DRY
- Shared error handling (T2.1: `CliErrorHandler`)
- Shared output formatting (T2.3: `JsonFormatter`)
- Shared tool invocation (T2.2: `ToolInvoker`)
- Command base class (T1.6: `BaseCliCommand`)
- Reuse existing `MetaTools`, not duplicate tool logic

### Boy-Scout
- Every merge includes format check (T5.4)
- Linting integrated into quality gates
- Test coverage required for new code (≥ 90% for CLI code)
- Architecture decisions documented (T4.4)
- No compiler warnings (T5.4)

---

## Testing Strategy

**Unit Tests (Mocked, Fast):**
- ConfigLoader precedence rules (T1.3)
- Command rendering (T1.7, T1.8)
- Error mapping (T2.1)
- Tool invocation (T2.2)
- Output formatting (T2.3)
- Query command paths (T2.4)
- RPC frame handling (T3.1)
- Parity assertions (T4.1)
- Edge cases (T5.3)

**Integration Tests (Real APIs, Slower):**
- Brewery search live call (T4.2)
- CLI vs. HTTP result parity (T4.2)
- Stdio bridge end-to-end (T4.2)

**Manual Tests:**
- `--help` output (T5.2)
- Startup latency (T4.3)
- README examples (T3.3)

**Total Test Coverage Target:** ≥ 90% on new CLI code; ≥ 70% overall including existing tests.
