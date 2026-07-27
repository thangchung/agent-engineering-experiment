<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**IMPORTANT: This project has a knowledge graph. ALWAYS use the
code-review-graph MCP tools BEFORE using Grep/Glob/Read to explore
the codebase.** The graph is faster, cheaper (fewer tokens), and gives
you structural context (callers, dependents, test coverage) that file
scanning cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes_tool` or `query_graph_tool` instead of Grep
- **Understanding impact**: `get_impact_radius_tool` instead of manually tracing imports
- **Code review**: `detect_changes_tool` + `get_review_context_tool` instead of reading entire files
- **Finding relationships**: `query_graph_tool` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview_tool` + `list_communities_tool`

Fall back to Grep/Glob/Read **only** when the graph doesn't cover what you need.

### Key Tools

| Tool | Use when |
| ------ | ---------- |
| `detect_changes_tool` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context_tool` | Need source snippets for review — token-efficient |
| `get_impact_radius_tool` | Understanding blast radius of a change |
| `get_affected_flows_tool` | Finding which execution paths are impacted |
| `query_graph_tool` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes_tool` | Finding functions/classes by name or keyword |
| `get_architecture_overview_tool` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes_tool` for code review.
3. Use `get_affected_flows_tool` to understand impact.
4. Use `query_graph_tool` pattern="tests_for" to check coverage.

<!-- project context -->
## Project Context

AgenticTodo. Demo of dual Entra ID OBO chain. Human → gateway → TodoApi → gateway
(hop-1 OBO exchange) → TodoAgent → hop-2 Entra **Agent ID** OBO → gateway → TodoMcpServer
→ EF/SQLite. TodoAgent also hits gateway's LLM route → Foundry Azure OpenAI. **Full mesh**:
every hop through agentgateway v1.4.0-beta.1, not just ingress. Stack: .NET 10, Aspire,
agentgateway, MCP C# SDK, Microsoft Agent Framework.

`prd.md`, `arch.md`, `plan/feature-agentic-todo-obo-1.md` = source of truth. Ref them first
when stuck on decision/business req. Update them (dated entries) whenever feature/fix lands
— this repo has strong history of that, keep it up.

**Prove not guess.** Whole project built on verifying real source/live behavior, never
assuming from docs/memory. Real bugs found this way, not by reading Rust source cold:
- `jwtAuth`/`mcpAuthentication` audiences = bare client-ID GUID, never `api://` prefix.
- `oauthTokenExchange.host` MUST be full `https://` URI — bare `host:port` never enables TLS.
- MCP backend needs explicit `backendAuth: passthrough` — `mcpAuthentication` strips token by default.
- `llm.models[].name` must EXACT-match request's `model` field, not the real Azure deployment name.
- `groupMembershipClaims` is per-app-registration, not tenant-global — each hop's exchanged
  token is governed by its OWN audience app's setting. Set on TodoApi/TodoAgent/TodoMcpServer all 3.

Fixed ports on purpose: TodoApi 5001/TodoAgent 5002/TodoMcpServer 5003/gateway 3000/llm
4000/admin 16000. Not dynamic — gateway.yaml is static config (`host.docker.internal:<port>`),
and Entra needs a stable Scalar OAuth redirect URI. Don't "fix" this back to dynamic.

`.env.local` = COMMITTED placeholder template (dummy values, safe on GitHub). `.env` =
git-ignored, real secrets, written by `scripts/setup-entra-obo-chain.ps1`. Opposite of the
usual Next.js convention — don't get confused. `apphost.cs` layers `.env.local` then `.env`.

RBAC: SuperAdminGroup (full access) vs NormalUserGroup (read-only), via `groups` claim,
`RequireClaim` policy. Enforced independently at TodoApi + TodoAgent + TodoMcpServer
(defense in depth, no single point of trust). **Never put PII (admin email, user data) in
error responses** — got burned once, fixed to a generic message. Watch for this pattern
repeating elsewhere.

Live/Docker+Entra-dependent tests (`AspireMeshIntegrationTests`, `DescriptionGenerationEvalTests`)
use `[SkippableFact]`, skip clean without creds — don't make them hard-fail. Running both
live suites together needs `dotnet test -m:1` (parallel test projects collide on the fixed
ports above, not a real bug).

Caveman mode stays on. Load skills when user says "must load X skill" — don't skip it.
