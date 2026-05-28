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

- Prefer minimal, surgical changes and avoid broad refactors unless requested.
- Link to existing docs instead of copying large sections into responses or new files.
- For third-party API usage, use the get-api-docs skill workflow to fetch current references before coding.
- When working on C#/.NET files, follow the scoped instruction file linked above.
- Apply strict red-green testing for all scenarios affected by a change: write or update a failing test first, implement the smallest fix, then run tests to green.
- Enforce KISS/YAGNI strictly: choose simplest implementation needed now; do not add speculative abstractions, extensibility points, or features.

## Structure

```
foundry-agentfx/
├── src/
│   ├── Claw.Api/              # ASP.NET host (Slack, Web endpoints)
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
User → Claw.Api → OrderingAgent → ToolSearch.Gateway → {Coffeeshop.Mcp, Foundry IQ, Toolbox}
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
