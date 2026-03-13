# CLI MCP Implementation Checklist

## How To Use
- Mark each item complete only when its validation command passes.
- Keep evidence notes short and factual.

## Phase A - Build Prerequisites

- [x] Add FrameworkReference Microsoft.AspNetCore.App in src/CoffeeshopCli/CoffeeshopCli.csproj
- [x] Add PackageReference ModelContextProtocol.AspNetCore in src/CoffeeshopCli/CoffeeshopCli.csproj
- [x] Run: dotnet restore
- [x] Run: dotnet build
- [x] Evidence:
	- Verified in src/CoffeeshopCli/CoffeeshopCli.csproj
	- Ran: dotnet restore && dotnet build
	- Result: build succeeded

## Phase B - Configuration Model And Loader

- [x] Add HostingConfig and CliConfig.Hosting in src/CoffeeshopCli/Configuration/CliConfig.cs
- [x] ConfigLoader loads from appsettings.json
- [x] ConfigLoader loads from appsettings.{ASPNETCORE_ENVIRONMENT}.json
- [x] ConfigLoader loads from environment variables
- [x] Legacy ~/.config/coffeeshop-cli/config.json loading removed
- [x] Legacy COFFEESHOP_ prefixed path assumptions removed
- [x] Add src/CoffeeshopCli/appsettings.json defaults
- [x] Run ConfigLoader tests
- [x] Evidence:
	- Verified in src/CoffeeshopCli/Configuration/CliConfig.cs and src/CoffeeshopCli/Configuration/ConfigLoader.cs
	- Verified appsettings file: src/CoffeeshopCli/appsettings.json
	- Ran: dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"
	- Result: Passed

## Phase C - Dual-Mode Entrypoint

- [x] Program.cs routes to CLI mode when Hosting:EnableHttpMcpBridge=false
- [x] Program.cs routes to HTTP bridge mode when Hosting:EnableHttpMcpBridge=true
- [x] Run: dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- skills list
- [x] Verify local CLI behavior unchanged
- [x] Evidence:
	- Verified in src/CoffeeshopCli/Program.cs (RunCliMode/RunHttpBridgeAsync)
	- Ran: dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- skills list
	- Result: listed 3 skills in CLI mode

## Phase D - HTTP MCP Bridge And Tools

- [x] Register AddMcpServer().WithHttpTransport().WithTools<CoffeeshopMcpBridgeTools>()
- [x] Map health endpoint from Hosting:HealthRoute
- [x] Map MCP endpoint from Hosting:HttpMcpRoute
- [x] Create src/CoffeeshopCli/Mcp/CoffeeshopMcpBridgeTools.cs
- [x] Add tool skill_list
- [x] Add tool skill_show
- [x] Add tool skill_invoke
- [x] Add tool menu_list_items
- [x] Add tool customer_lookup
- [x] Add tool order_submit
- [x] skill_invoke returns confirmation_required when confirm=false
- [x] skill_invoke rejects unsupported intent with intent_not_supported_for_submit
- [x] Evidence:
	- Verified in src/CoffeeshopCli/Program.cs and src/CoffeeshopCli/Mcp/CoffeeshopMcpBridgeTools.cs
	- Started bridge: Hosting__EnableHttpMcpBridge=true dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj
	- Health check: curl http://127.0.0.1:8080/healthz -> 200 Healthy
	- MCP initialize returned Mcp-Session-Id and serverInfo name/version
	- tools/list returned: skill_list, skill_show, skill_invoke, menu_list_items, customer_lookup, order_submit, order_submit
	- tools/call skill_invoke confirm=false returned message confirmation_required
	- tools/call skill_invoke intent=account returned error intent_not_supported_for_submit

## Phase E - Containerization

- [x] Create Dockerfile at repo root
- [ ] Build image: docker build -t coffeeshop-cli:latest .
- [ ] Run cloud mode container with Hosting__EnableHttpMcpBridge=true
- [ ] Verify /healthz returns success
- [ ] Verify /mcp handles tools/list request
- [x] Evidence:
	- Verified Dockerfile exists at repo root
	- Attempted: docker build -t coffeeshop-cli:latest .
	- Result: blocked in this environment (zsh: command not found: docker)
	- How to verify locally once docker is installed:
		1. docker build -t coffeeshop-cli:latest .
		2. docker run --rm -p 8080:8080 -e Hosting__EnableHttpMcpBridge=true coffeeshop-cli:latest
		3. curl http://127.0.0.1:8080/healthz
		4. initialize + tools/list call on /mcp with Mcp-Session-Id header

## Phase F - Skills Documentation Alignment

- [x] Update skills/coffeeshop-menu-guide/SKILL.md with MCP-first flow
- [x] Update skills/coffeeshop-customer-lookup/SKILL.md with MCP-first flow
- [x] Update skills/coffeeshop-counter-service/SKILL.md with MCP-first flow
- [x] Verify tool names match implemented MCP tools
- [x] Evidence:
	- Verified all 3 files updated under skills/
	- Verified names referenced in docs match implementation: menu_list_items, customer_lookup, order_submit, skill_invoke
	- Ran: dotnet run --project src/CoffeeshopCli/CoffeeshopCli.csproj -- skills list
	- Result: all 3 skills discovered with updated versions/descriptions

## Phase G - Acceptance Criteria Validation

- [x] AC1: EnableHttpMcpBridge=false keeps existing CLI behavior
- [ ] AC2: EnableHttpMcpBridge=true exposes /mcp and /healthz in container
- [x] AC3: Skills execute MCP-first with CLI fallback guidance
- [x] AC4: Mode switch controlled only by Hosting__EnableHttpMcpBridge override
- [x] Run full tests: dotnet test
- [x] Evidence:
	- AC1 verified with dotnet run ... skills list (default Hosting flag false)
	- AC2 partially blocked for container-only validation (docker unavailable), but host-process bridge mode validated with /healthz and /mcp
	- AC3 verified by updated skill docs and live tools/list + tools/call validation
	- AC4 verified by switching behavior using only Hosting__EnableHttpMcpBridge=true env var
	- Tests: dotnet restore && dotnet build && dotnet test -> all passed (38/38)

## Sign-Off

- [ ] Implementation complete
- [x] Evidence reviewed
- [ ] Ready for PR
