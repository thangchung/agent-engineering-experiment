# CLI MCP Implementation Tasks (Prioritized)

## Goal
Implement the dual-mode runtime defined in cli_mcp.md with minimal risk while preserving existing local CLI behavior.

## Priority Legend
- P0: Blocking for compile or core runtime behavior
- P1: Required for cloud MCP feature completeness
- P2: Delivery and operational hardening

## P0 - Core Runtime And Configuration

### T1. Add ASP.NET Core + MCP bridge dependencies
Priority: P0
Why: HTTP bridge cannot compile without these dependencies.
Scope:
- Update src/CoffeeshopCli/CoffeeshopCli.csproj
- Add FrameworkReference Microsoft.AspNetCore.App
- Add PackageReference ModelContextProtocol.AspNetCore
Exit criteria:
- dotnet build succeeds with WebApplication and AddMcpServer symbols resolved

### T2. Extend CLI config model with Hosting section
Priority: P0
Why: Program.cs mode switch requires Hosting settings.
Scope:
- Update src/CoffeeshopCli/Configuration/CliConfig.cs
- Add HostingConfig record and CliConfig.Hosting property
Exit criteria:
- cfg.Hosting is available and default values are applied

### T3. Replace ConfigLoader source strategy
Priority: P0
Why: Runtime mode and routes must come from appsettings + env vars.
Scope:
- Update src/CoffeeshopCli/Configuration/ConfigLoader.cs
- Load from appsettings.json, appsettings.{Environment}.json, and env vars
- Remove ~/.config/coffeeshop-cli/config.json and COFFEESHOP_ prefix assumptions
- Bind Discovery, Mcp, Hosting
Exit criteria:
- Hosting__EnableHttpMcpBridge=true flips mode
- Discovery:SkillsDirectory still resolves correctly

### T4. Add default appsettings
Priority: P0
Why: Single source of defaults for local and container mode.
Scope:
- Create src/CoffeeshopCli/appsettings.json
- Include Hosting and Discovery defaults from cli_mcp.md
Exit criteria:
- dotnet run works with no external config

### T5. Introduce dual-mode startup branch in Program.cs
Priority: P0
Why: This is the architectural pivot for CLI mode vs HTTP MCP bridge mode.
Scope:
- Update src/CoffeeshopCli/Program.cs
- Keep RunCliMode behavior unchanged
- Add RunHttpBridgeAsync with health route and MCP route mapping
Exit criteria:
- Hosting:EnableHttpMcpBridge=false runs CLI commands unchanged
- true starts HTTP server and exposes /healthz and /mcp

## P1 - Cloud MCP Tool Surface

### T6. Add CoffeeshopMcpBridgeTools class
Priority: P1
Why: Cloud clients need callable MCP tools in HTTP mode.
Scope:
- Create src/CoffeeshopCli/Mcp/CoffeeshopMcpBridgeTools.cs
- Add tools: skill_list, skill_show, skill_invoke, menu_list_items, customer_lookup, order_submit
- Add records: OrderLineInput, SkillInvokeArgs
Exit criteria:
- tools/list returns all expected names
- tools/call succeeds for each tool path

### T7. Register HTTP-mode services only
Priority: P1
Why: Keep mode-specific DI clean and avoid unnecessary dependencies in cloud mode.
Scope:
- In RunHttpBridgeAsync register only required services:
  CliConfig, ModelRegistry, IDiscoveryService, SkillParser, OrderSubmitHandler, HealthChecks
Exit criteria:
- HTTP mode boots cleanly without CLI-only services

### T8. Enforce non-interactive skill invoke contract
Priority: P1
Why: skill_invoke must be deterministic and safe over MCP.
Scope:
- Validate supported skill name and intent
- Require explicit confirmation before submit
- Return structured errors for unsupported intent/skill
Exit criteria:
- process-order with confirm=true submits
- confirm=false returns confirmation_required state
- unsupported intent returns intent_not_supported_for_submit

## P2 - Deployment And Skill Artifacts

### T9. Add Dockerfile for dual-mode runtime
Priority: P2
Why: Required for containerized cloud deployment and local parity testing.
Scope:
- Create repo-root Dockerfile per cli_mcp.md
- Publish app in build stage, run in aspnet runtime stage
Exit criteria:
- docker run with Hosting__EnableHttpMcpBridge=true exposes /healthz and /mcp

### T10. Update skill manifests for MCP-first fallback behavior
Priority: P2
Why: Skill docs should match cloud tool contract and fallback strategy.
Scope:
- Update:
  skills/coffeeshop-menu-guide/SKILL.md
  skills/coffeeshop-customer-lookup/SKILL.md
  skills/coffeeshop-counter-service/SKILL.md
- Ensure MCP-first calls and CLI fallback guidance are consistent
Exit criteria:
- skill docs reference menu_list_items, customer_lookup, order_submit, skill_invoke consistently

### T11. Add/adjust tests for new behavior
Priority: P2
Why: Protect core mode-switch and config loading behavior.
Scope:
- Update ConfigLoader tests for appsettings + env var behavior
- Add tests for new tool behaviors where feasible
Exit criteria:
- dotnet test passes
- coverage includes mode flag and skill_invoke contract branches

### T12. Verify acceptance criteria end-to-end
Priority: P2
Why: Final release gate.
Scope:
- Validate all four acceptance criteria from cli_mcp.md in local and container modes
Exit criteria:
- all criteria verified and documented

## Suggested Execution Order
1. T1 -> T2 -> T3 -> T4 -> T5
2. T6 -> T7 -> T8
3. T9 -> T10 -> T11 -> T12

## Risks And Mitigations
- Risk: SDK/package version mismatch for ModelContextProtocol.AspNetCore
  Mitigation: pin confirmed version and run restore/build immediately after T1
- Risk: Config key casing drift (Discovery vs discovery)
  Mitigation: use consistent section names and add targeted tests
- Risk: Behavioral regression in CLI mode
  Mitigation: preserve RunCliMode registrations and run command integration tests
