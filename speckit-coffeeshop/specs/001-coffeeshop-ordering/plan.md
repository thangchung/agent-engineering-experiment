# Implementation Plan: CoffeeShop Ordering System

**Branch**: `001-coffeeshop-ordering` | **Date**: 2026-02-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-coffeeshop-ordering/spec.md`

## Summary

Build a full coffeeshop ordering system: customers are identified by email, ID, or
order ID; they browse the product catalog (sourced from an out-of-process MCP
Server) via a hybrid chat + structured UI; place, confirm, track, modify, and
cancel orders; and receive real-time streaming status updates as barista and kitchen
workers prepare their items.

**Technical approach**: .NET 10 Minimal API backend with three runtime components (counter, product-catalog, frontend), orchestrated by .NET Aspire 13.x. Counter hosts BaristaWorker and KitchenWorker as in-process IHostedService workers and is the sole HTTP entry point and the MAF orchestrating agent; it dispatches work to BaristaWorker and KitchenWorker (in-process `IHostedService`) via `Channel<T>` and queries the product-catalog via MCP over HTTP/SSE. The frontend is Next.js with CopilotKit (hybrid chat sidebar +
structured menu/order page). All order fulfilment uses `RunStreamingAsync` with
AG-UI protocol streaming. All state is in-memory; no persistence.

---

## Technical Context

**Language/Version**: Backend: C# 13 / .NET 10 | Frontend: TypeScript (strict) / Next.js App Router | Orchestrator: .NET Aspire 13.x

**Architecture**: 3-component runtime architecture — counter (HTTP + MAF orchestrating agent + in-process BaristaWorker + KitchenWorker), product-catalog (out-process MCP Server), frontend (Next.js). BaristaWorker and KitchenWorker are IHostedService classes inside counter — they are not separate runtime components. counter↔workers: in-process `System.Threading.Channels.Channel<T>`. counter↔product-catalog: MCP over HTTP/SSE.

**Agent Framework**: Microsoft Agent Framework (`Microsoft.Agents.AI`) for counter, barista, kitchen. LLM provider: GitHub Copilot SDK via `copilotClient.AsAIAgent(sessionConfig, ownsClient: false, ...)` — no Azure credentials or `GITHUB_TOKEN` env var needed; Copilot CLI handles auth. Chat-client turn lifecycle also via Copilot SDK (`CreateSessionAsync` / `SendAsync`). Patterns from `github/awesome-copilot/cookbook/copilot-sdk/dotnet` and `agent-framework/.../Agent_With_GitHubCopilot`.

**Primary Dependencies**:
- Backend: `Microsoft.Agents.AI`, `GitHub.Copilot.SDK` (counter only), `xUnit`, `WebApplicationFactory` (integration tests only — MUST NOT appear in production `Program.cs` or application code), `OpenTelemetry.Exporter.OpenTelemetryProtocol` (via ServiceDefaults)
- Frontend: `@copilotkit/react-core`, `@copilotkit/react-ui`, `shadcn UI`, `Tailwind CSS`, `Vitest`, `React Testing Library`, `@vercel/otel`, `@opentelemetry/sdk-web`, `@opentelemetry/exporter-trace-otlp-http`
- AppHost: `Aspire.Hosting.NodeJs`

**Storage**: In-memory only — all domain state (customers, orders, menu items, session intent) is held in process-local singleton collections in the counter service. No external database, cache, or file persistence (Constitution VII).

**Testing**: Unit: xUnit (backend) + Vitest/React Testing Library (frontend) | Integration: xUnit + `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`, shared `tests/integration/` project)

**Target Platform**: Web (local dev via Aspire AppHost; backend: .NET 10 on Linux/Windows; frontend: browser via Next.js dev server)

**Project Type**: Web application (multi-component backend + Next.js frontend + Aspire AppHost)

**Performance Goals**:
- Menu retrieval (MCP call to product-catalog): <200ms p95
- Order placement (counter → confirm): <500ms p95 excluding streaming time
- Status query: <100ms p95
- Streaming progress updates: each update emitted within 2 seconds of the corresponding processing event (per SC and FR-020)

**Constraints**: Minimal dependencies (Constitution I); no Newtonsoft; no custom CSS unless Tailwind insufficient; no hard-coded `localhost` ports (use Aspire service discovery); no direct LLM HTTP calls (use MAF agent loop); no EF Core; no file I/O for persistence

**Scale/Scope**: Single coffeeshop, local-dev scale. 3 seed customers, 11 seed menu items, 3 seed orders. Runtime orders use `ORD-{6000–9999}` format. Per-item quantity limit: 5.

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Minimal Dependencies** | ✅ PASS | Dependencies are the minimum required set: MAF + Copilot SDK (agent runtime + LLM), CopilotKit (AG-UI + hybrid UI), OTel (observability mandate), shadcn/Tailwind (UI mandate), xUnit/Vitest/Aspire.Hosting.Testing (test mandate). No redundant packages. |
| **II. API-First Design** | ✅ PASS | All counter endpoints defined in `contracts/` before implementation. Explicit request/response record types required. Routes versioned under `/api/v1/`. Frontend consumes via typed service clients in `src/lib/`. |
| **III. Test-First (NON-NEGOTIABLE)** | ✅ PASS | TDD cycle mandated. All 5 user stories have acceptance scenarios suitable for test-first. Integration tests via `DistributedApplicationTestingBuilder`. Unit tests for all public use-case surface. |
| **IV. Component-Based UI** | ✅ PASS | Frontend built from shadcn primitives; Tailwind for all styling; WCAG 2.1 AA required. CopilotKit chat sidebar + structured page satisfies hybrid UX (FR-017). |
| **V. Observability** | ✅ PASS | All backend services call `AddServiceDefaults()` + `MapDefaultEndpoints()`. Frontend initializes OTel in `instrumentation.ts`. OTLP endpoint injected by Aspire. RFC 9457 Problem Details for errors. `servicedefaults/Extensions.cs` MUST also register `MapHealthChecks("/healthz")` as an alias alongside `/health` to satisfy constitution Principle V MUST requirement. |
| **VI. Simplicity (YAGNI)** | ✅ PASS | No auth (FR-019 explicitly out of scope), no persistence, no tier-based pricing/priority, no real-time queue depth, no caching fallback (FR-021 fail-fast). All spec items solve stated requirements. |
| **VII. In-Memory State (NON-NEGOTIABLE)** | ✅ PASS | `ConcurrentDictionary` singletons for customers, orders. `IReadOnlyList<T>` for product catalog. Seed data loaded at startup from `SeedData.cs`. No EF Core, no Dapper, no file I/O. |

**Gate result**: ALL PASS — no violations. Proceed to Phase 0.

---

## Project Structure

### Documentation (this feature)

```text
specs/001-coffeeshop-ordering/
├── plan.md              # This file
├── spec.md              # Feature specification (24 FRs, 5 user stories, 8 SCs)
├── research.md          # Phase 0 output — architecture & integration findings
├── data-model.md        # Phase 1 output — entities, state, seed data
├── quickstart.md        # Phase 1 output — setup & run instructions
├── contracts/           # Phase 1 output — typed API contracts (counter endpoints)
│   ├── README.md
│   ├── customers.md
│   ├── orders.md
│   └── menu.md
├── checklists/          # Existing checklists
└── tasks.md             # Phase 2 output — /speckit.tasks (NOT created here)
```

### Source Code (repository root)

```text
coffeeshop/
├── apphost/                          # .NET Aspire App Host
│   ├── Program.cs                    #   Resource registrations + WithReference() wiring
│   └── apphost.csproj
│
├── servicedefaults/                  # Shared Aspire ServiceDefaults (OTel, health, discovery)
│   ├── Extensions.cs
│   └── servicedefaults.csproj
│
├── backend/
│   ├── counter/                      # In-process: HTTP entry point + orchestrating agent
│   │   ├── Domain/                   #   Customer, Order, OrderItem, OrderStatus, ItemType
│   │   ├── Application/              #   Use cases, MAF agent instructions, tool definitions
│   │   │   ├── UseCases/             #   LookupCustomer, PlaceOrder, TrackOrder, etc.
│   │   │   └── Agents/               #   CounterAgent (instructions + tools)
│   │   ├── Infrastructure/           #   InMemoryCustomerStore, InMemoryOrderStore,
│   │   │   │                         #   SeedData, BaristaChannel, KitchenChannel,
│   │   │   │                         #   McpProductCatalogClient
│   │   │   └── ...
│   │   ├── Endpoints/                #   Minimal API route registrations (/api/v1/...)
│   │   ├── Workers/                  #   BaristaWorker, KitchenWorker (IHostedService — in-process)
│   │   │   ├── BaristaWorker.cs      #     Channel<OrderDispatch> reader; invokes BaristaAgent
│   │   │   └── KitchenWorker.cs      #     Channel<OrderDispatch> reader; invokes KitchenAgent
│   │   ├── Program.cs
│   │   └── counter.csproj
│   │
│   └── product-catalog/              # Out-of-process: MCP Server (HTTP/SSE)
│       ├── Program.cs                #   FastMCP-style server; exposes item types, categories, prices
│       └── product-catalog.csproj
│
├── frontend/                         # Next.js App Router (TypeScript strict)
│   ├── src/
│   │   ├── app/
│   │   │   ├── layout.tsx            #   <CopilotKit> provider root
│   │   │   ├── page.tsx              #   Menu grid + order summary + chat sidebar
│   │   │   └── api/
│   │   │       └── copilotkit/
│   │   │           └── route.ts      #   AG-UI endpoint → counter HTTP
│   │   ├── components/
│   │   │   ├── copilot/              #   Generative UI (useCopilotAction)
│   │   │   └── ui/                   #   shadcn primitives
│   │   ├── lib/                      #   Typed service clients, CopilotKit utilities
│   │   ├── types/                    #   Shared TypeScript interfaces
│   │   └── instrumentation.ts        #   OTel init
│   ├── tests/                        #   Vitest + React Testing Library
│   ├── package.json
│   ├── tailwind.config.ts
│   └── tsconfig.json
│
├── tests/
│   ├── integration/                  # Aspire.Hosting.Testing (xUnit)
│   │   ├── BackendApiTests.cs        #   Counter API: customers, orders, menu
│   │   ├── FrontEndTests.cs          #   Frontend smoke tests
│   │   ├── OrderFulfilmentTests.cs   #   Streaming + Channel dispatch (mixed/bev/food/empty)
│   │   └── tests.csproj
│   └── unit/                         #   xUnit — isolated use-case tests
│
└── CoffeeShop.sln
```

**Structure Decision**: Multi-component backend (Option 2 variant) with Aspire AppHost as the single orchestrator. All service-to-service URLs are resolved via Aspire service discovery. The frontend is a Node resource in the AppHost (`AddNpmApp`).

---

## Phase 0 — Research

> **Status**: COMPLETE. See [`research.md`](research.md).

**All NEEDS CLARIFICATION items resolved**:

| Item | Resolution |
|------|-----------|
| MAF + Copilot SDK wiring | `copilotClient.AsAIAgent()` pattern from `Agent_With_GitHubCopilot` sample. `CopilotClient` singleton, `ownsClient: false`. No ProviderConfig needed for the MAF path. |
| AG-UI streaming boundaries | 4 boundaries: (a) catalog lookup, (b) dispatch, (c) per-worker completion, (d) final result. `RunStreamingAsync` mandatory; `RunAsync` banned for fulfilment. |
| `Others` category routing | Routes to kitchen, identical to `Food`. |
| MCP unreachability strategy | Fail-fast: no cache, no fallback. Return user-facing error. |
| Cancellation boundary | Before `preparing` only. Same boundary as modification (FR-010/011/022). |
| Per-item quantity | Max 5 per item per order (FR-024). |
| Product-catalog query scope | Counter queries catalog to classify items by category for routing. Catalog data: key, name, category, price, availability. |
| Parallel mixed-order dispatch | `Task.WhenAll` both channels. Await both completions before responding. |
| Session ID strategy | `counter-{customerId}` as meaningful, stable `SessionId`. |
| Frontend state sync | `useCopilotReadable` (state exposure) + `useCopilotAction` (agent-driven mutations). No custom state management. |

---

## Phase 1 — Design & Contracts

> **Status**: COMPLETE. Artifacts below.

### Deliverables

- [`research.md`](research.md) — architecture confidence + gap analysis
- [`data-model.md`](data-model.md) — entities, enums, state transitions, seed data
- [`contracts/README.md`](contracts/README.md) — contract index
- [`contracts/customers.md`](contracts/customers.md) — customer lookup endpoints
- [`contracts/orders.md`](contracts/orders.md) — order CRUD + streaming endpoints
- [`contracts/menu.md`](contracts/menu.md) — product catalog endpoint
- [`quickstart.md`](quickstart.md) — prerequisites, setup, run instructions

### Post-Design Constitution Re-check

All Phase 1 design decisions comply with the constitution:

- Data model uses only `ConcurrentDictionary` / `IReadOnlyList<T>` — no persistence constructs
- Contracts define explicit record types — no anonymous objects
- All endpoints versioned under `/api/v1/`
- Streaming contracts specify AG-UI SSE response type
- No auth headers on any endpoint (FR-019)
- Seed data defined in-code in `SeedData.cs` — no file source

**Gate result**: ALL PASS — proceed to tasks phase (`/speckit.tasks`).

---

## AppHost Wiring Requirements

**Project reference graph** (governs `CoffeeShop.sln` project references and `AddProject`/`WithReference` wiring):

```
apphost             → counter, product-catalog, frontend (all registered)
counter.csproj      → servicedefaults
product-catalog.csproj → servicedefaults
tests/integration   → apphost (via Aspire.Hosting.Testing)
tests/unit          → counter (domain/application layers only)
```
> **Note**: `barista.csproj` and `kitchen.csproj` do **not** exist. BaristaWorker and KitchenWorker are `IHostedService` classes compiled into `counter.csproj`. They share `counter`'s DI container and `Channel<T>` singletons. No separate Aspire project registration is needed or permitted.

**`apphost/Program.cs` required scaffold**:

```csharp
var catalog  = builder.AddProject<Projects.ProductCatalog>("product-catalog");
var counter  = builder.AddProject<Projects.Counter>("counter")
                      .WithReference(catalog)
                      .WithExternalHttpEndpoints();
var frontend = builder.AddNpmApp("frontend", "../frontend", "dev")
                      .WithReference(counter)
                      .WithEnvironment("COUNTER_URL", counter.GetEndpoint("http"))
                      .WithHttpEndpoint(env: "PORT");
// BaristaWorker and KitchenWorker are IHostedService classes inside counter — no AddProject.
```

**Aspire resource names** (strings used in service discovery, `WithReference`, and dashboard):

| Resource | Aspire Name | Type | References |
|----------|-------------|------|------------|
| counter | `"counter"` | AddProject | product-catalog |
| product-catalog | `"product-catalog"` | AddProject | — |
| frontend | `"frontend"` | AddNpmApp | counter |

> **Not registered**: barista and kitchen are `IHostedService` workers inside `counter` — they have no independent Aspire resource entry.

**Frontend environment variable injection**: Aspire injects the counter's HTTP endpoint URL into the Next.js Node resource as `COUNTER_URL`. The `route.ts` AG-UI bridge uses `process.env.COUNTER_URL` as the forwarding base URL. `NEXT_PUBLIC_` prefix is NOT used (server-side only). `<CopilotKit runtimeUrl>` is set to the Next.js-relative path `/api/copilotkit` — not directly to the Aspire-injected counter URL.

**`Aspire.Hosting.NodeJs` version**: Match the Aspire 13.x SDK version in the apphost. Use `dotnet add package Aspire.Hosting.NodeJs` — NuGet resolves the compatible version from the installed Aspire workload automatically.

**Health check endpoints** (surfaced by `MapDefaultEndpoints()` in all .NET services):
- `GET /health` — aggregate; returns 200 if all registered checks pass. Aspire dashboard uses this for resource status.
- `GET /alive` — liveness probe; returns 200 if process is running. No dependency checks.
- `GET /healthz` — alias for `/health`; fulfils constitution Principle V MUST requirement. Register alongside `/health` via `MapHealthChecks("/healthz")` in `servicedefaults/Extensions.cs`.

All backend service projects (**counter** and **product-catalog**) MUST call `builder.AddServiceDefaults()` in `Program.cs`.

**ServiceDefaults canonical configuration** (`servicedefaults/Extensions.cs` — single source, no per-service OTel config):
- OTel traces: `AddAspNetCoreInstrumentation()`, `AddHttpClientInstrumentation()`
- OTel metrics: `AddAspNetCoreInstrumentation()`
- OTLP exporter: endpoint from `OTEL_EXPORTER_OTLP_ENDPOINT` env var (Aspire-injected — do not hardcode)
- Health: `AddHealthChecks()` + `MapHealthChecks("/health")` + liveness at `MapHealthChecks("/alive")`

**Testable no-hardcoded-ports criterion**: Any string literal matching `http://localhost:[0-9]+` or `https://localhost:[0-9]+` in production source (excluding `*.json` launch settings and test projects) is a pull-request blocker. All inter-service URLs MUST come from Aspire service discovery env vars or `WithReference()`-injected endpoint URIs.

---

## Channel\<T\> Technical Requirements

**Ownership**: Both channels are created in `counter/Program.cs` DI registration and injected as singletons into barista and kitchen via constructor injection (two dispatch channels + two reply channels).

**Dispatch message type** (counter → worker):

```csharp
public record OrderDispatch(
    string OrderId,
    string CustomerId,
    IReadOnlyList<OrderItem> Items,   // only the items routed to this worker
    string CorrelationId              // matches reply back to awaiting call
);
```

**Reply message type** (worker → counter):

```csharp
public record WorkerResult(
    string  CorrelationId,
    bool    Success,
    string? ErrorMessage   // null if Success = true
);
```

**`CorrelationId` generation**: Counter generates `CorrelationId = $"{order.Id}-{Guid.NewGuid():N}"` immediately before writing each dispatch message. The GUID suffix guarantees uniqueness across concurrent orders and any future retry paths.

**Channel configuration**:

```csharp
Channel.CreateUnbounded<OrderDispatch>(
    new UnboundedChannelOptions { SingleWriter = false, SingleReader = true });
// Two dispatch channels (BaristaChannel, KitchenChannel)
// Two reply channels (BaristaReplyChannel, KitchenReplyChannel)
```

Unbounded is acceptable at local-dev scale (Constitution VI — YAGNI).

**Parallel dispatch** (binding requirement, not just a research finding): For orders containing both Beverages and Food/Others items, counter MUST `Task.WhenAll` both dispatch-and-await-reply operations simultaneously. Sequential dispatch is prohibited.

**Worker timeout**: Counter MUST apply a `CancellationToken` with a **30-second timeout** when awaiting each reply channel. On timeout:
1. Order status → `Cancelled`
2. User-facing error: `"Order preparation timed out — please contact staff."`

**Partial Task.WhenAll failure** (one worker succeeds, one fails or times out): Counter MUST treat this as a full failure:
1. Order status → `Cancelled`
2. User-facing error: `"Order could not be completed — please reorder."`
3. No partial completion state is persisted. This satisfies SC-008.

**Error propagation**: Worker exceptions are caught in the channel consumer loop and written as `WorkerResult { Success = false, ErrorMessage = ... }` to the reply channel. They do NOT propagate unhandled to the ASP.NET request thread.

---

## Agent Instructions & Tools

### CounterAgent — Required System Prompt Content

The `CounterAgent` `Instructions` property MUST include all of the following:

1. **Role**: "You are the counter agent at CoffeeShop. Help customers identify themselves, browse the menu, and manage their orders."
2. **Routing rules**: "Route Beverages items to barista. Route Food and Others items to kitchen."
3. **Modification/cancellation boundary**: "Orders can only be modified or cancelled before `preparing`. If the boundary has passed, refuse and show the current status."
4. **Pickup-time rule**: "Beverages-only orders: 'Ready in about 5 minutes'. Any food or others item: 'Ready in about 10 minutes'."
5. **MCP fail-fast rule**: "If the product catalog is unavailable, inform the customer and do not proceed to place the order."
6. **Identification requirement**: "Always identify the customer before any order action. If no identifier is provided, ask for their email or order number."

### CounterAgent — Tool Definitions (all required — none optional)

| Tool Name | Parameters | Return Type | Spec |
|-----------|-----------|-------------|------|
| `LookupCustomer` | `identifier: string` | `CustomerLookupResponse` | FR-001–003 |
| `GetMenu` | — | `MenuResponse` | FR-004 |
| `PlaceOrder` | `customerId: string, items: OrderItemRequest[], notes?: string` | `OrderDto` | FR-005–008, FR-020 |
| `GetOrderStatus` | `orderId: string` | `OrderDto` | FR-012 |
| `GetOrderHistory` | `customerId: string` | `OrderHistoryResponse` | FR-013–014 |
| `ModifyOrder` | `orderId: string, notes?: string, items?: OrderItemRequest[]` | `OrderDto` | FR-010–011 |
| `CancelOrder` | `orderId: string` | `CancelOrderResponse` | FR-022 |

**Session ID** (binding requirement): Counter MUST use `SessionId = $"counter-{customerId}"` when constructing the MAF agent session. This ensures multi-turn conversation continuity per customer.

### BaristaAgent & KitchenAgent — Required Behaviour

Both worker services MUST:
1. Run a `Channel<OrderDispatch>` reader loop in a hosted background service (`IHostedService`).
2. For each `OrderDispatch`, invoke `RunStreamingAsync` with the worker MAF agent. Worker `RunStreamingAsync` output is consumed internally for OTel spans and structured logs only — workers do **not** emit AG-UI events directly to the frontend.
3. Include in worker agent instructions: role, item scope (`Beverages` / `Food + Others`), and completion signal obligation.
4. Write `WorkerResult` to the reply channel upon completion (success or caught exception).
5. Never communicate directly with the frontend or counter HTTP layer.

---

## MCP product-catalog Interface

### MCP Tools Exposed by product-catalog (binding interface contract)

| MCP Tool | Input Schema | Return Schema |
|----------|-------------|--------------|
| `get_menu_items` | `{}` (no params) | `MenuItemDto[]` |
| `get_item_by_type` | `{ "type": "string" }` | `MenuItemDto \| null` |

**`MenuItemDto` shape** (identical to `GET /api/v1/menu` response items — no mapping layer needed):

```json
{ "type": "LATTE", "displayName": "LATTE", "category": "Beverages", "price": 4.50, "isAvailable": true }
```

### `isAvailable` Semantics

- `isAvailable: true` — item can be ordered; display normally.
- `isAvailable: false` — item MUST still appear in `GET /api/v1/menu` responses and in the frontend menu grid (display with a visual unavailability indicator, e.g., greyed-out or "Unavailable" badge). MUST be rejected at `POST /api/v1/orders` with HTTP 409 (FR-016). **Never filter unavailable items from catalog responses.**

### MCP Transport

- product-catalog registered in Aspire as `"product-catalog"` via `builder.AddProject`.
- Counter discovers it via `WithReference(catalog)` — Aspire injects the service URL as an env var.
- Counter uses `McpProductCatalogClient` — a typed `HttpClient` with Aspire-injected base URL. No hardcoded URL permitted.
- 5-second per-call timeout on `McpProductCatalogClient`. Timeout → fail-fast 503 response (FR-021).

---

## Frontend Technical Requirements

### Component Hierarchy

```
layout.tsx
└─ <CopilotKit runtimeUrl="/api/copilotkit">   ← Next.js-relative path; NOT Aspire-injected
   └─ page.tsx
      ├─ <CustomerPanel />      useCopilotReadable("customer", { id, firstName, tier })
      ├─ <MenuGrid />           useCopilotReadable("menuItems", MenuItemDto[])
      ├─ <OrderSummaryPanel />  useCopilotReadable("currentOrder", OrderDto | null)
      │   └─ <StatusBadge />    role="status" aria-live="polite"
      └─ <CopilotSidebar />     ← @copilotkit/react-ui built-in
```

`<CopilotKit runtimeUrl>` MUST point to `/api/copilotkit` (same-origin Next.js route). The route forwards to the counter via `process.env.COUNTER_URL`. Do NOT set `runtimeUrl` to the counter URL directly.

### `useCopilotReadable` Required Registrations

| Key | Shape | Used by |
|-----|-------|---------|
| `"customer"` | `{ id: string, firstName: string, tier: string } \| null` | CustomerPanel |
| `"menuItems"` | `MenuItemDto[]` | MenuGrid |
| `"currentOrder"` | `OrderDto \| null` | OrderSummaryPanel |
| `"orderHistory"` | `OrderDto[]` | OrderHistoryList |

### `useCopilotAction` Required Handlers (all MUST be present)

| Action name | Trigger | Required side-effect |
|-------------|---------|---------------------|
| `setCustomer` | Agent identifies a customer | Set `customer` state |
| `updateMenu` | Agent fetches product catalog | Set `menuItems` state |
| `updateOrderSummary` | Agent builds or modifies order | Set `currentOrder` state |
| `updateOrderStatus` | Streaming status event received | Update `currentOrder.status` + render in `<StatusBadge>` |
| `showOrderHistory` | Agent retrieves order history | Set `orderHistory` state |

### `route.ts` AG-UI Bridging

`frontend/src/app/api/copilotkit/route.ts` MUST:
- Use `CopilotRuntime` from `@copilotkit/react-core/server`.
- Forward all requests to `${process.env.COUNTER_URL}/api/v1/copilotkit`.
- Response Content-Type: `text/event-stream` (SSE). Do not buffer; pipe through directly.

### Empty-State Requirements

| Area | Trigger | Required message |
|------|---------|------------------|
| Menu grid | product-catalog 503 | "Menu is currently unavailable — please try again shortly." |
| Menu grid | catalog returns 0 items | "No items available at this time." |
| Order summary | No active order | Panel hidden or shows a placeholder prompt |
| Order history | No prior orders | "No orders yet — let's fix that!" (FR-014) |

### TypeScript Strict Mode

`tsconfig.json` MUST include `"strict": true`. Type assertions (`value as T`) are prohibited without an inline comment justifying safety. This is a pull-request review gate.

### OTel Initialisation (`instrumentation.ts`)

MUST configure:
- Provider: `@vercel/otel` + `@opentelemetry/exporter-trace-otlp-http`
- Service name: `"coffeeshop-frontend"`
- OTLP endpoint: `process.env.OTEL_EXPORTER_OTLP_ENDPOINT` (Aspire-injected — do not hardcode)
- Instrumented spans: page loads, `fetch()` calls to `/api/copilotkit` and counter REST endpoints

### WCAG 2.1 AA Requirements

| Component | Required attribute / pattern |
|-----------|------------------------------|
| `<MenuGrid>` item button | `aria-label="{displayName} — ${price}"` + keyboard-activatable (Enter/Space) |
| `<OrderSummaryPanel>` | `aria-live="polite"` on status update region |
| `<StatusBadge>` | `role="status"` + `aria-live="polite"` |
| CopilotKit chat input | Validate `aria-label="Order assistant"` is present |

---

## Concurrency & Atomicity Requirements

### `ConcurrentDictionary` Write Rules

All writes to `InMemoryOrderStore` and `InMemoryCustomerStore` MUST use thread-safe methods:
- New records: `TryAdd(key, value)` — reject if key already exists.
- Status transitions: `TryUpdate(key, newValue, comparisonValue)` or `AddOrUpdate` — atomic compare-and-swap ensures only one concurrent transition wins.
- Direct index assignment (`dict[key] = value`) is **prohibited** on all production store dictionaries. A code reviewer MUST reject any PR that violates this.

### Pre-placement Validation Sequence (SC-008)

Order confirmation MUST follow this exact sequence:

1. Validate order is non-empty (FR-015) — fail 400 immediately.
2. Validate all quantities 1–5 (FR-024) — fail 400 immediately.
3. Call product-catalog `get_menu_items` to confirm all selected items have `isAvailable: true` (fail-fast 503 if MCP unreachable — FR-021; fail 409 if any item unavailable — FR-016).
4. Construct the `Order` object in local memory — NOT yet written to the store.
5. `InMemoryOrderStore.TryAdd(order.Id, order)` — only after ALL validation in steps 1–3 passes.
6. Dispatch to worker channels only after step 5 succeeds.

If any step 1–3 fails, the local `Order` object is discarded and the store is never written. This guarantees no partial order state is ever observable (SC-008).

