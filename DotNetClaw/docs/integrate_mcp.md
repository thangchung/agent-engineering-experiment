# MCP Integration Outcome (Skill Loader)

## What changed

- Added dual backend support in `SkillLoaderTool`:
  - `cli` mode (existing behavior): calls `coffeeshop-cli skills list/show --json` through `ExecTool`.
  - `mcp` mode (new behavior): connects to coffeeshop HTTP MCP bridge via SSE session, then calls MCP `tools/call` with:
    - `skill_list`
    - `skill_show`
- Added appsettings switches under `CoffeeshopCli` to choose mode and MCP endpoint.

## New config (appsettings.json)

```json
"CoffeeshopCli": {
  "Mode": "cli",
  "ExecutablePath": "../../coffeeshop-cli/src/CoffeeshopCli/bin/Debug/net10.0/CoffeeshopCli",
  "Mcp": {
    "BaseUrl": "http://127.0.0.1:8080",
    "SsePath": "/mcp/sse",
    "RequestTimeoutSeconds": 20
  }
}
```

## How to switch

- Keep current behavior: `CoffeeshopCli:Mode = cli`
- Use MCP bridge: `CoffeeshopCli:Mode = mcp`
  - Ensure coffeeshop-cli is running with HTTP bridge enabled.

## References

- coffeeshop MCP bridge entrypoint: `coffeeshop-cli/src/CoffeeshopCli/Program.cs`
- bridge tool names/contracts: `coffeeshop-cli/src/CoffeeshopCli/Mcp/CoffeeshopMcpBridgeTools.cs`
- DotNetClaw dual-mode implementation: `DotNetClaw/SkillLoaderTool.cs`
- DotNetClaw config switch: `DotNetClaw/appsettings.json`
