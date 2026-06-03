# AGENTS.md — foundry-agentfx

## Mandatory Skills

**Apply on every prompt:**
- `karpathy-guidelines` — judge all code changes
- `code-simplification` — judge all refactors
- `aspire` — judge orchestration/infra
- `aspireify` — adding services to Aspire
- `dotnet-inspect` — NuGet/API usage
- `playwright-cli` — E2E testing

## Stack

- **Lang:** C# / .NET 10
- **Framework:** Microsoft Agent Framework (MAF) + Azure Foundry SDK
- **MCP:** ModelContextProtocol.NET (HTTP transport only)
- **Infra:** Azure Container Apps, Aspire, Bicep, azd
- **Observability:** OpenTelemetry (GenAI + MCP semconv)

## Principles

1. **Foundry-first** — Use `AIProjectClient` + `DefaultAzureCredential` as primary provider
2. **Gateway pattern** — Agent sees only `search_tools` + `call_tool`; backend MCP servers hidden
3. **Red-green test** — Write failing test → implement → pass → refactor
4. **OTel everywhere** — Traces, metrics, logs follow GenAI semantic conventions
5. **No CLI** — Coffeeshop.Mcp is pure HTTP MCP server, no subprocess calls

## Conventions For AI Coding Agents

Local reasoning + bounded blast radius > cleverness or premature reuse. Flag rule conflicts before writing; ask which takes priority.

- Surgical changes only; no broad refactors unless requested.
- Link docs; don't copy large sections into responses or new files.
- Use get-api-docs skill for third-party API usage before coding.

**1. Explicit Dependencies**  
All outside-world deps (config, clients, creds, clocks, env) passed as args. No module-level globals, no implicit init ordering. Signature must show every dep.

**2. Types as Contracts**  
No `any`/untyped dicts at module boundaries. Failable fns return Result type; errors = discriminated unions, one named variant per failure mode. Write signature before impl.

**3. Tests as Specification**  
Write tests before impl. Show test list (happy path + boundaries + failure modes); wait for review before coding. Never modify a test to fit broken impl — flag it explicitly. Red-green-refactor on every affected scenario.

**4. Fail Fast, Fail Loud**  
Validate at every public fn entry. Named exceptions on bad data. No silent fallbacks, no defaults masking missing data. Catch → re-raise or convert to domain error with structured context.

**5. Vertical Slices, Strong Boundaries**  
Organize by feature (routes + logic + data + types + tests in one folder). Features don't import from other features. No shared utils/helpers for new code — duplicate instead.

**6. Rule of Three**  
Duplicate before abstracting. Extract shared abstraction only when same pattern appears in 3 distinct real places. KISS/YAGNI: simplest impl now; no speculative abstractions or extensibility points.

## Structure

```
foundry-agentfx/
├── src/
│   ├── Claw.Agent/              # ASP.NET host (Slack, Web endpoints)
│   ├── Claw.Core/             # Runtime, agents, MAF workflows
│   ├── Claw.Channels/         # Channel adapters
│   ├── Coffeeshop.Mcp/        # MCP server (menu, orders, customers)
│   ├── Coffeeshop.Models/     # Domain models
│   ├── ToolSearch.Gateway/    # Tool-search MCP gateway
│   └── ServiceDefaults/       # Aspire defaults (OTel, health)
├── apphost/
│   └── AppHost/               # Aspire orchestration
├── tests/
│   ├── Claw.Tests/
│   ├── Coffeeshop.Tests/
│   └── ToolSearch.Tests/
├── skills/                    # SKILL.md manifests
├── mind/                      # Agent identity (system prompts)
├── infra/                     # Bicep templates
└── foundry-agentfx.slnx
```

## Key Flows

```
User → Claw.Agent → OrderingAgent → ToolSearch.Gateway → {Coffeeshop.Mcp, Foundry IQ, Toolbox}
                        ↓
                  AuditAgent → orders/*.md
```

## Tool-Search Pattern

Agent gets 2 synthetic tools:
- `search_tools(query)` → returns matching tool descriptors
- `call_tool(name, args)` → executes tool via gateway

Token savings: ~95% (2 tools in context vs 20+)

## OTel Conventions

| Attribute | Value |
|-----------|-------|
| `mcp.method.name` | `tools/call`, `tools/list` |
| `gen_ai.operation.name` | `execute_tool` |
| `gen_ai.tool.name` | Tool being called |

Metrics: `mcp.client.operation.duration`, `gen_ai.client.token.usage`

## Commands

```bash
# Build
dotnet build foundry-agentfx.slnx

# Test
dotnet test

# Run locally (Aspire)
dotnet run --project apphost/AppHost

# Deploy
azd up

# Verify: dotnet build passes, curl POST /mcp works, POST /api/chat responds, orders/*.md created
```
