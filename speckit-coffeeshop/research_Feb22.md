# Research Report: CoffeeShop System — Architecture & Integration Confidence Assessment

**Date**: 2026-02-22
**Source documents**:
- `.specify/memory/constitution.md` (v1.6.2)
- `specs/001-coffeeshop-ordering/spec.md` (24 FRs, 5 user stories, 8 SCs)

---

## 1. Executive Summary

The CoffeeShop system is an agentic ordering application composed of five
runtime components orchestrated by .NET Aspire. A C#/.NET 10 backend uses
Microsoft Agent Framework (MAF) with GitHub Copilot SDK as the LLM provider;
a TypeScript/Next.js frontend uses CopilotKit for a hybrid chat + structured
UI. The product catalog runs as a standalone MCP Server.

**Overall integration confidence: HIGH** — the architecture is well-defined
with clear communication contracts, a single orchestrator (Aspire AppHost),
and explicit rules eliminating ambiguity. Three areas require careful
implementation attention (detailed in §7).

---

## 2. Component Inventory

| # | Component | Runtime | Process Model | Primary Role |
|---|-----------|---------|---------------|--------------|
| 1 | **apphost** | .NET Aspire 13.x | Orchestrator | Registers all resources, wires service discovery, injects env vars (URLs, OTLP), starts everything with `dotnet run --project apphost/` |
| 2 | **counter** | .NET 10 Minimal API | In-process | MAF orchestrating agent. Sole HTTP entry point. Owns customer lookup, intent classification, order CRUD, dispatch to barista/kitchen, streaming status via AG-UI |
| 3 | **barista** | .NET 10 | In-process | MAF agent. Consumes beverage work items from `Channel<T>`. Signals completion back to counter |
| 4 | **kitchen** | .NET 10 | In-process | MAF agent. Consumes food/others work items from `Channel<T>`. Signals completion back to counter |
| 5 | **product-catalog** | .NET 10 MCP Server | Out-of-process | Exposes item types, categories, and prices via MCP over HTTP/SSE. Stateless reference data |
| 6 | **frontend** | Next.js (App Router) | Out-of-process (Node) | TypeScript strict, shadcn UI, Tailwind CSS. CopilotKit chat sidebar + structured page. AG-UI endpoint proxies to counter |
| 7 | **servicedefaults** | .NET library | Shared | OTel SDK setup, health endpoints, service discovery extensions. Referenced by all backend projects |

---

## 3. Communication Topology

```
┌─────────────────────────────────────────────────────────────────────┐
│                        .NET Aspire AppHost                         │
│  (service discovery, OTLP injection, resource lifecycle)           │
└────────────┬────────────┬────────────┬────────────┬────────────────┘
             │            │            │            │
             ▼            ▼            ▼            ▼
        ┌─────────┐  ┌────────┐  ┌────────┐  ┌──────────────────┐
        │ counter  │  │barista │  │kitchen │  │ product-catalog  │
        │ (HTTP)   │  │(worker)│  │(worker)│  │ (MCP Server)     │
        └────┬─────┘  └───▲────┘  └───▲────┘  └──────▲───────────┘
             │             │           │              │
             ├─Channel<T>──┘           │              │
             ├─Channel<T>──────────────┘              │
             ├─MCP HTTP/SSE───────────────────────────┘
             │
    AG-UI    │    HTTP (Minimal API)
   streaming │
             ▼
        ┌──────────┐
        │ frontend  │
        │ (Next.js) │
        └──────────┘
```

### Protocol Details

| Path | Protocol | Transport | Sync/Async |
|------|----------|-----------|------------|
| frontend → counter | HTTP + AG-UI streaming | Aspire service discovery URL | Request-response with SSE streaming |
| counter → product-catalog | MCP over HTTP/SSE | Aspire service discovery URL | Sync (request/response) |
| counter → barista | `System.Threading.Channels.Channel<T>` | In-process memory | Async (fire + await completion) |
| counter → kitchen | `System.Threading.Channels.Channel<T>` | In-process memory | Async (fire + await completion) |
| barista → counter | `Channel<T>` (completion signal) | In-process memory | Async callback |
| kitchen → counter | `Channel<T>` (completion signal) | In-process memory | Async callback |

---

## 4. Order Fulfilment Flow (Deep Analysis)

The flow is the heart of the system. Six discrete steps:

1. **Receive** — frontend POSTs order via AG-UI → counter Minimal API endpoint
2. **Classify** — counter's MAF agent queries product-catalog (MCP) to resolve
   item categories. Streams "looking up menu…" to frontend
3. **Route** — counter splits line items:
   - `Beverages` → barista `Channel<T>`
   - `Food` / `Others` → kitchen `Channel<T>`
   - Mixed → both in parallel (`Task.WhenAll`)
   - Empty → immediate no-action response
4. **Stream progress** — `RunStreamingAsync` pushes AG-UI events at each
   boundary: dispatch, per-worker completion, catalog lookup
5. **Complete** — workers write completion signals to response channels;
   counter aggregates
6. **Notify** — counter updates in-memory order status → streams final
   "order ready" → ends AG-UI stream

### Critical Design Decisions

- **Streaming is mandatory** — `RunAsync` is explicitly banned for fulfilment;
  only `RunStreamingAsync` is permitted. This ensures the frontend always
  receives progressive updates.
- **Parallel dispatch for mixed orders** — `Task.WhenAll` on both channels.
  If one worker fails, counter still awaits the other before returning a
  partial-failure status.
- **No direct method calls** — all counter↔worker communication goes through
  channels. This enforces clean separation even though all three run in the
  same process.

---

## 5. Technology Integration Map

### 5.1 MAF + Copilot SDK Integration

The GitHub Copilot SDK serves dual roles:

1. **MAF LLM Provider** — `copilotClient.AsAIAgent()` bridges Copilot SDK into
   MAF's `AIAgent` abstraction. No Azure credentials or GitHub Models
   endpoint needed; the Copilot CLI handles auth transparently.

2. **Chat Turn Handler** — `CreateSessionAsync` / `SendAsync` / event
   subscriptions manage conversational turns. Sessions use meaningful IDs
   (`counter-{customerId}`) and are disposed after each turn.

**Key singleton pattern**: `CopilotClient` is registered once in DI, started
at app startup. All agents share it with `ownsClient: false`.

### 5.2 CopilotKit + AG-UI Integration

The frontend uses CopilotKit for a hybrid UX:

- **Chat sidebar** — conversational interface (`@copilotkit/react-ui`)
- **Structured page** — menu grid, order summary panel updated reactively
  via `useCopilotReadable` (state exposure) and `useCopilotAction` (agent-
  driven mutations)
- **AG-UI endpoint** — `src/app/api/copilotkit/route.ts` proxies requests
  to the counter's HTTP endpoint. Counter URL comes from Aspire env var.
- **Generative UI** — components in `src/components/copilot/` are rendered
  from agent tool calls

### 5.3 MCP Server (Product Catalog)

- Runs out-of-process as a separate .NET project
- FastMCP-style server exposing item types, categories, and prices
- Counter acts as MCP client; URL resolved via Aspire service discovery
- Fail-fast on unreachability (FR-021): no cache, no fallback

### 5.4 Aspire Orchestration

The AppHost wires everything together:

```csharp
var productCatalog = builder.AddProject<Projects.ProductCatalog>("product-catalog");
var counter = builder.AddProject<Projects.Counter>("counter").WithReference(productCatalog);
builder.AddProject<Projects.Barista>("barista").WithReference(counter);
builder.AddProject<Projects.Kitchen>("kitchen").WithReference(counter);
builder.AddNpmApp("frontend", "../frontend", "dev").WithReference(counter).WithHttpEndpoint(env: "PORT");
```

Key guarantees:
- Service discovery eliminates all hardcoded URLs
- OTLP endpoint + service name auto-injected into every resource
- `dotnet run --project apphost/` is the single startup command
- Aspire Dashboard provides traces, logs, and metrics out of the box

### 5.5 Observability

- **Backend**: ServiceDefaults project provides OTel SDK, OTLP exporter,
  ASP.NET Core + HTTP instrumentation. Every backend service calls
  `AddServiceDefaults()` + `MapDefaultEndpoints()`.
- **Frontend**: `@vercel/otel` (server-side) + `@opentelemetry/sdk-web`
  (browser-side). OTLP endpoint from Aspire env var.
- **Correlation**: Backend errors include correlation IDs (RFC 9457 Problem
  Details). Frontend records client errors as OTel span events.

---

## 6. Data Model & State Management

### In-Memory Storage (Constitution VII — NON-NEGOTIABLE)

All state lives in `ConcurrentDictionary` singletons:
- `InMemoryCustomerStore.cs` — 3 seed customers (Alice/gold, Bob/silver, Carol/standard)
- `InMemoryOrderStore.cs` — 3 seed orders (ORD-5001 completed, ORD-5002 preparing, ORD-5003 pending)
- Product catalog: `IReadOnlyList<T>` singleton — 11 items across 3 categories

**State resets on restart** — no persistence, no ORM, no file I/O. Tests
seed their own state.

### Order Lifecycle

```
pending → confirmed → preparing → ready → completed
                  ↘ cancelled (only before preparing)
```

- Modification/cancellation boundary: `confirmed` (before `preparing`)
- Runtime order IDs: `ORD-{random 6000–9999}`
- Per-item quantity limit: 5 units max
- Pickup estimates: beverages-only ≤5 min, food/mixed ≤10 min

### Key Entities

| Entity | Key Attributes |
|--------|---------------|
| Customer | ID (`C-XXXX`), name, email, phone, tier (display-only) |
| Product/Menu Item | Key (enum), display name, category, price, availability |
| Order | ID (`ORD-XXXX`), customer ref, line items, total, status, notes, timestamp |

---

## 7. Integration Confidence Assessment

### HIGH CONFIDENCE Areas

| Area | Why |
|------|-----|
| **Aspire orchestration** | Single `Program.cs` with explicit `WithReference()` chains. Service discovery eliminates URL mismatches. Well-established pattern in .NET 10 ecosystem. |
| **In-memory storage** | No external dependencies = zero infrastructure failures. Thread-safe `ConcurrentDictionary` singletons. Seed data is deterministic. |
| **Counter → barista/kitchen (Channel\<T\>)** | `System.Threading.Channels` is a battle-tested .NET primitive. In-process = no serialization overhead, no network failures. `Task.WhenAll` for parallel dispatch is idiomatic. |
| **Clean architecture** | Each component has Domain/Application/Infrastructure layers. Boundaries are well-defined. Agent instructions live in Application, tools in Application/Infrastructure. |
| **Testing strategy** | Aspire integration tests bring up the full stack identically to production. `WaitForResourceAsync` prevents race conditions. No `WebApplicationFactory` for cross-service tests. |
| **Observability** | ServiceDefaults centralizes OTel config. OTLP injection is automatic. Dashboard is free with Aspire. |

### MEDIUM CONFIDENCE Areas (require careful implementation)

| Area | Concern | Mitigation |
|------|---------|------------|
| **MAF + Copilot SDK wiring** | The `copilotClient.AsAIAgent()` pattern is relatively new. Dual-role (MAF provider + chat turn handler) adds complexity. Session lifecycle (create → send → dispose per turn) must be correct to avoid resource leaks. | Follow the canonical `Agent_With_GitHubCopilot` sample exactly. Rigorous integration tests for session cleanup. Register `CopilotClient` as singleton with shutdown hook. |
| **AG-UI streaming fidelity** | Counter must emit status updates at 4 boundaries (catalog lookup, dispatch, per-worker completion, final result). Frontend must render each progressively. Timing and ordering of stream events need precise coordination. | Define a typed enum/contract for stream event types. Test with mixed orders (both workers). Verify per-worker completion events arrive independently. |
| **MCP Server unreachability** | Product-catalog is out-of-process. HTTP/SSE transport can fail. FR-021 mandates fail-fast, but the MCP client error handling path needs robust implementation. | Wrap MCP calls with timeout + structured error handling. Integration test: stop product-catalog resource, verify counter returns proper error. |

### LOWER CONFIDENCE Areas (potential friction points)

| Area | Concern | Recommendation |
|------|---------|----------------|
| **CopilotKit shared state synchronization** | `useCopilotReadable` / `useCopilotAction` must keep the structured page in sync with agent state. If the agent updates order state but CopilotKit state lags, the UI becomes inconsistent. | Implement optimistic UI updates with agent-driven confirmation. Test state sync under rapid sequential actions (add item → remove item → confirm). |
| **Copilot CLI dependency** | Authentication is handled by GitHub Copilot CLI which must be in PATH. In CI or fresh dev environments, this is an implicit prerequisite that could cause opaque failures. | Explicit prerequisite check script. Catch `FileNotFoundException` at startup and surface a clear "install Copilot CLI" message. Document in README. |
| **`ProviderConfig` inconsistency in constitution** | The MAF section correctly states no `ProviderConfig` needed (v1.5.2 correction), but the Copilot SDK section still shows a `ProviderConfig` with `GITHUB_TOKEN` in the code sample. This contradicts the MAF section's claim that "no GITHUB_TOKEN env var" is needed. | Resolve the contradiction: if using `AsAIAgent()` (MAF path), no ProviderConfig is needed. If using direct `CreateSessionAsync` (chat turn path), ProviderConfig IS needed. Document both paths clearly with when-to-use guidance. |

---

## 8. Spec Coverage Analysis

### FR → Architecture Mapping

| FR | Component(s) | Confidence |
|----|-------------|------------|
| FR-001–003 (customer lookup) | counter (Application/Infrastructure) | HIGH — simple in-memory lookup |
| FR-004 (menu display) | counter → product-catalog (MCP) | MEDIUM — MCP dependency |
| FR-005–007 (order creation) | counter (Application/Infrastructure) | HIGH — in-memory CRUD |
| FR-008 (pickup time) | counter (Application) | HIGH — category-based fixed string |
| FR-009 (pre-confirm modification) | counter (Application) | HIGH — in-memory state mutation |
| FR-010–011 (post-confirm modification boundary) | counter (Application) | HIGH — status check gate |
| FR-012–014 (status + history) | counter (Application/Infrastructure) | HIGH — in-memory query |
| FR-015–016 (validation) | counter (Application) | HIGH — guard clauses |
| FR-017 (hybrid UX) | frontend (CopilotKit) | MEDIUM — shared state sync |
| FR-018 (tier display) | counter + frontend | HIGH — display-only |
| FR-019 (no auth) | counter (Endpoints) | HIGH — nothing to implement |
| FR-020 (streaming status) | counter (MAF RunStreamingAsync) + frontend (AG-UI) | MEDIUM — streaming coordination |
| FR-021 (MCP fail-fast) | counter (Infrastructure/MCP client) | MEDIUM — error handling path |
| FR-022 (cancellation) | counter (Application) | HIGH — status check + update |
| FR-023 (Others → kitchen routing) | counter (Application) | HIGH — category mapping |
| FR-024 (quantity limit = 5) | counter (Application) | HIGH — validation |

### Gaps Identified

1. **No FR for order status transitions** — the lifecycle
   (`pending → confirmed → preparing → ready → completed | cancelled`) is
   defined in entities but there's no FR explicitly governing which
   transitions are valid. Risk: invalid state transitions in edge cases.

2. **No FR for concurrent order handling** — edge cases mention "two
   customers simultaneously" but no FR defines the concurrency guarantee.
   `ConcurrentDictionary` provides thread safety, but order ID uniqueness
   under concurrency should be explicitly tested.

3. **No success criterion for streaming** — SC-001 through SC-008 cover
   functional outcomes but none measure streaming latency or completeness.
   FR-020 mandates streaming but there's no SC validating that updates
   arrive within a time bound.

4. **Spec still contains `in_preparation`** — User Story 3 scenario 1 says
   `"in preparation"` and User Story 5 scenarios 2–3 use `"in preparation"`.
   Constitution standardized on `preparing`. These should be normalized.

---

## 9. Risk Matrix

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Copilot CLI not installed in dev/CI | Medium | High (total failure) | Startup prerequisite check, clear error message |
| MCP Server crashes mid-request | Low | Medium (single order fails) | FR-021 fail-fast + retry prompt |
| Channel\<T\> deadlock on mixed orders | Low | High (system hangs) | Timeout on `Task.WhenAll`, integration test for mixed orders |
| CopilotKit state desync | Medium | Medium (UI inconsistency) | Optimistic updates + agent-driven reconciliation |
| Session leak (CopilotClient) | Medium | High (resource exhaustion) | Dispose-after-turn rule, `using`/`await using` pattern |
| Constitution-spec terminology drift | Low | Low (confusion) | Normalize `in_preparation` → `preparing` in spec |

---

## 10. Conclusions

### Can the components work well together?

**Yes — with disciplined implementation.** The architecture is sound:

- **Aspire** provides the "glue" — service discovery, OTLP injection, single
  startup command. This eliminates an entire class of integration bugs
  (wrong URLs, missing env vars, manual startup ordering).

- **Channel\<T\>** for in-process async communication is the right choice for
  barista/kitchen workers. It's lightweight, type-safe, and avoids the
  overhead of external message brokers while satisfying Constitution VII.

- **MCP for product-catalog** is well-motivated — it's the one truly
  independent service (stateless reference data), and keeping it out-of-process
  demonstrates the MCP protocol pattern without over-engineering the worker
  communication.

- **MAF + Copilot SDK** is the most novel integration point. The
  `AsAIAgent()` bridge pattern is well-documented in the canonical sample.
  The dual-role complexity (MAF provider vs. chat turn handler) is manageable
  if the two concerns are kept in separate classes.

### Top 3 implementation priorities

1. **Get the Aspire AppHost wiring working first** — this validates that all
   5 resources start, discover each other, and the dashboard shows traces.

2. **Implement the Channel\<T\> dispatch + completion loop** — this is the
   architectural backbone. Test with beverages-only, food-only, mixed, and
   empty orders before adding the MAF agent layer on top.

3. **Integrate MAF streaming early** — FR-020 (streaming status) affects
   the entire fulfilment flow. Don't bolt it on later; build it into the
   counter's agent loop from day one using `RunStreamingAsync`.
